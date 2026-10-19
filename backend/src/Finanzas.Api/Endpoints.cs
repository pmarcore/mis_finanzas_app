using Finanzas.Domain;
using Finanzas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Api;

public static class Endpoints
{
    // La fecha de corte no se guarda: si no viene, es hoy.
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Filtro opcional de listados: "familia" o "memey"; vacío trae los dos ámbitos (null).</summary>
    public static Scope? ParseScope(string? s) => s?.ToLowerInvariant() switch { "familia" => Scope.Familia, "memey" => Scope.Memey, _ => null };

    /// <summary>Saldos y reportes son siempre de un ámbito (no hay vista total): sin indicar, Familia.</summary>
    public static Scope ScopeOrFamilia(string? s) => ParseScope(s) ?? Scope.Familia;

    /// <summary>Reglas comunes al cargar un movimiento: tipo válido, tarjeta solo donde corresponde.</summary>
    static string? Validate(Transaction t, OperationType? type, PaymentMethod? method)
    {
        if (type is null) return "Elegí un tipo de operación válido.";
        if (method is null) return "Cada movimiento necesita un medio de pago válido.";
        if (method.IsCard && type.Kind is OperationKind.PagoTarjeta or OperationKind.Transferencia or OperationKind.Ahorro or OperationKind.Inversion)
            return $"{type.Name} sale de una caja, no de una tarjeta.";
        if (type.Kind == OperationKind.PagoTarjeta && (t.PaidCardId is null || t.FirstDueDate is null))
            return "Un pago de tarjeta necesita la tarjeta y el vencimiento que paga.";
        return null;
    }

    /// <summary>Valida el movimiento, completa el vencimiento propuesto si es con tarjeta y arma sus impactos en caja.</summary>
    static async Task<string?> BuildImpacts(FinanzasDbContext db, Transaction t)
    {
        var method = await db.PaymentMethods.Include(m => m.Cycles).FirstOrDefaultAsync(m => m.Id == t.PaymentMethodId);
        var type = await db.OperationTypes.FindAsync(t.OperationTypeId);
        if (Validate(t, type, method) is { } error) return error;
        t.OperationType = type!;
        if (type!.Kind != OperationKind.PagoTarjeta) t.PaidCardId = null;

        if (method!.IsCard)
        {
            t.FirstDueDate ??= CardCalendar.ProposeDueDate(method.Cycles, t.Date);
            if (t.FirstDueDate is null) return "Falta cargar el cierre de la tarjeta para esa fecha en Configuración.";
        }
        else
        {
            // Sin tarjeta no hay cuotas y sale de la caja en la fecha; solo un pago de tarjeta guarda el vencimiento que paga.
            t.Installments = 1;
            if (type.Kind != OperationKind.PagoTarjeta) t.FirstDueDate = null;
        }

        var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
        t.Impacts = CashImpactBuilder.Build(t, method, s => boxes[s]);
        return null;
    }

    static Task<Dictionary<int, string>> CardNames(FinanzasDbContext db) =>
        db.PaymentMethods.Where(m => m.Type == PaymentMethodType.Tarjeta).ToDictionaryAsync(m => m.Id, m => m.Name);

    /// <summary>Calculadora de caja con el gasto estimado del presupuesto familiar, igual para el dashboard y el flujo de caja.</summary>
    static async Task<(CashFlowCalculator Calc, List<BudgetEstimate> Estimates)> Calculator(
        FinanzasDbContext db, List<Category> categories, List<Budget> budgets, List<Transaction> transactions, DateOnly cutoff, DateOnly horizon)
    {
        var rules = await db.RecurringRules.Include(r => r.Amounts).ToListAsync();
        var estimates = Budgeting.MonthlyEstimates(categories, budgets, rules, Scope.Familia, cutoff, horizon);
        return (new CashFlowCalculator(await db.CashBoxes.ToListAsync(), transactions, await db.Statements.ToListAsync(), estimates), estimates);
    }

    /// <summary>
    /// Flujo de caja del mes del corte con saldo acumulado. El saldo en la línea de corte es la caja actual del dashboard
    /// y el saldo al final ('until', por defecto fin de mes) es la caja a fin de mes.
    /// </summary>
    public static void MapCashFlow(this WebApplication app) => app.MapGet("/cashflow", async (string? scope, DateOnly? date, DateOnly? until, FinanzasDbContext db) =>
    {
        var cutoff = date ?? Today();
        var end = until ?? Months.EndOf(cutoff);
        if (end < cutoff) return Results.BadRequest("'until' no puede ser anterior a la fecha de corte.");
        var sc = ScopeOrFamilia(scope);
        var transactions = await db.Transactions.Include(t => t.Impacts).ToListAsync();
        var cards = await CardNames(db);
        var (calc, _) = await Calculator(db, await db.Categories.ToListAsync(), await db.Budgets.ToListAsync(), transactions, cutoff, end);
        var ledger = calc.Ledger(sc, cutoff, end);

        var balance = ledger.Opening;
        object Line(CashLedgerLine l, bool pending)
        {
            balance += l.Amount;
            var t = l.Transaction;
            var kind = t is not null ? "movimiento" : l.CardId is not null ? "resumenTarjeta" : "gastoEstimado";
            var description = t is not null ? t.Description
                : l.CardId is { } c ? $"Resumen {cards.GetValueOrDefault(c, "tarjeta")}"
                : "Gasto estimado del presupuesto";
            return new
            {
                date = l.Date,
                kind,
                transactionId = t?.Id,
                scope = t?.Scope,
                operationTypeId = t?.OperationTypeId,
                categoryId = t?.CategoryId,
                status = t?.Status,
                cardId = l.CardId,
                paidCardId = t?.PaidCardId,
                description,
                amount = Money.Pesos(l.Amount),
                balance = Money.Pesos(balance),
                pending,
            };
        }
        var done = ledger.Done.Select(l => Line(l, false)).ToList();
        var cashNow = balance;
        var pending = ledger.Pending.Select(l => Line(l, true)).ToList();

        return Results.Ok(new
        {
            scope = sc,
            from = ledger.From,
            cutoff,
            until = end,
            opening = Money.Pesos(ledger.Opening),
            cashNow = Money.Pesos(cashNow),
            projected = Money.Pesos(balance),
            lines = done.Concat(pending),
        });
    });

    public static void MapDashboard(this WebApplication app) => app.MapGet("/dashboard", async (string? scope, DateOnly? date, FinanzasDbContext db) =>
    {
        var cutoff = date ?? Today();
        var sc = ScopeOrFamilia(scope);
        var categories = await db.Categories.ToListAsync();
        var budgets = await db.Budgets.ToListAsync();
        var transactions = await db.Transactions.Include(t => t.Impacts).ToListAsync();
        var cards = await CardNames(db);

        var horizon = Months.EndOf(cutoff.AddMonths(5));
        var (calc, estimates) = await Calculator(db, categories, budgets, transactions, cutoff, horizon);

        var endOfMonth = Months.EndOf(cutoff);
        var projectedEom = calc.ProjectedAt(sc, cutoff, endOfMonth);
        var quarter = Enumerable.Range(0, 3)
            .Select(k => Months.EndOf(cutoff.AddMonths(k)))
            .Select(d => new { month = Months.Format(d), balance = calc.ProjectedAt(sc, cutoff, d) })
            .ToList();

        var cardPayments = calc.CardStatements(sc, cutoff, horizon)
            .Where(s => s.Outstanding > 0)
            .GroupBy(s => Months.Format(s.DueDate))
            .Select(g => new
            {
                dueDate = g.Min(s => s.DueDate),
                total = Money.Pesos(g.Sum(s => s.Outstanding)),
                cards = g.Select(s => cards.GetValueOrDefault(s.CardId, "?")).Distinct().ToList(),
                byCard = g.Select(s => new { cardId = s.CardId, card = cards.GetValueOrDefault(s.CardId, "?"), dueDate = s.DueDate, total = Money.Pesos(s.Outstanding) }).ToList(),
            })
            .ToList();


        return Results.Ok(new
        {
            cutoff,
            scope = sc,
            cashNow = Money.Pesos(calc.CashAt(sc, cutoff)),
            projectedEndOfMonth = Money.Pesos(projectedEom),
            quarter = quarter.Select(q => new { q.month, balance = Money.Pesos(q.balance) }),
            estimatedMonthlySpend = estimates.Select(e => new { month = Months.Format(e.Date), amount = Money.Pesos(e.Amount) }),
            memey = new
            {
                cashNow = Money.Pesos(calc.CashAt(Scope.Memey, cutoff)),
                projectedEndOfMonth = Money.Pesos(calc.ProjectedAt(Scope.Memey, cutoff, endOfMonth)),
            },
            cardPayments,
        });
    });

    public static void MapTransactions(this WebApplication app)
    {
        var tx = app.MapGroup("/transactions");

        tx.MapGet("/", async (string? scope, DateOnly? from, DateOnly? to, TransactionStatus? status, int? categoryId, int? paymentMethodId, FinanzasDbContext db) =>
        {
            var sc = ParseScope(scope);
            var list = await db.Transactions
                .Where(t => (sc == null || t.Scope == sc) && (from == null || t.Date >= from) && (to == null || t.Date <= to) && (status == null || t.Status == status))
                .Where(t => (categoryId == null || t.CategoryId == categoryId) && (paymentMethodId == null || t.PaymentMethodId == paymentMethodId))
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();
            return list.Select(TransactionDto.From);
        });

        tx.MapGet("/{id:int}", async (int id, FinanzasDbContext db) =>
            await db.Transactions.FindAsync(id) is { } t ? Results.Ok(TransactionDto.From(t)) : Results.NotFound());

        tx.MapPost("/", async (NewTransactionDto body, FinanzasDbContext db) =>
        {
            var input = body.ToEntity();
            if (await BuildImpacts(db, input) is { } error) return Results.BadRequest(error);
            db.Transactions.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/transactions/{input.Id}", TransactionDto.From(input));
        });

        // Editar recalcula los impactos en caja; saldos, resúmenes de tarjeta, proyecciones y presupuesto vs. real
        // se calculan al leer, así que reflejan el cambio sin más pasos.
        tx.MapPut("/{id:int}", async (int id, NewTransactionDto body, FinanzasDbContext db) =>
        {
            var t = await db.Transactions.Include(x => x.Impacts).FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return Results.NotFound();
            var edited = body.ToEntity();
            // Lo que no viene en el formulario se conserva: el gasto fijo de origen, la importación y si es compromiso previo.
            edited.RecurringRuleId = t.RecurringRuleId;
            edited.ImportId = t.ImportId;
            edited.IsPriorCommitment = t.IsPriorCommitment;
            if (await BuildImpacts(db, edited) is { } error) return Results.BadRequest(error);

            db.CashImpacts.RemoveRange(t.Impacts);
            t.Scope = edited.Scope; t.PersonId = edited.PersonId; t.OperationTypeId = edited.OperationTypeId; t.OperationType = edited.OperationType;
            t.Date = edited.Date; t.Description = edited.Description; t.CategoryId = edited.CategoryId; t.Amount = edited.Amount;
            t.PaymentMethodId = edited.PaymentMethodId; t.Installments = edited.Installments; t.FirstDueDate = edited.FirstDueDate;
            t.Status = edited.Status; t.PaidCardId = edited.PaidCardId;
            t.Impacts = edited.Impacts;
            await db.SaveChangesAsync();
            return Results.Ok(TransactionDto.From(t));
        });

        tx.MapPost("/{id:int}/confirm", async (int id, ConfirmRequest? req, FinanzasDbContext db) =>
        {
            var t = await db.Transactions.Include(x => x.Impacts).FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return Results.NotFound();
            var method = await db.PaymentMethods.Include(m => m.Cycles).FirstAsync(m => m.Id == t.PaymentMethodId);
            var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);

            t.Status = TransactionStatus.Realizado;
            t.Date = req?.Date ?? t.Date;
            if (req?.Amount is { } a) t.Amount = Money.Cents(a);
            if (method.IsCard && t.OperationType.Kind != OperationKind.PagoTarjeta && t.RecurringRuleId is not null)
                t.FirstDueDate = CardCalendar.ProposeDueDate(method.Cycles, t.Date) ?? t.FirstDueDate;
            db.CashImpacts.RemoveRange(t.Impacts);
            t.Impacts = CashImpactBuilder.Build(t, method, s => boxes[s]);
            await db.SaveChangesAsync();
            return Results.Ok(TransactionDto.From(t));
        });

        // Borrar se lleva sus impactos en caja: la caja, el resumen de tarjeta y el presupuesto dejan de contarlo.
        tx.MapDelete("/{id:int}", async (int id, FinanzasDbContext db) =>
        {
            var t = await db.Transactions.Include(x => x.Impacts).FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return Results.NotFound();
            db.Transactions.Remove(t);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    public static void MapCards(this WebApplication app)
    {
        var cards = app.MapGroup("/cards");

        cards.MapGet("/{id:int}/due-date", async (int id, DateOnly purchaseDate, FinanzasDbContext db) =>
        {
            var cycles = await db.CardCycles.Where(c => c.PaymentMethodId == id).ToListAsync();
            var due = CardCalendar.ProposeDueDate(cycles, purchaseDate);
            return due is { } d ? Results.Ok(new CardDueDateDto(id, purchaseDate, d)) : Results.NotFound("No hay cierre cargado para esa fecha.");
        });

        cards.MapGet("/{id:int}/cycles", async (int id, FinanzasDbContext db) =>
            (await db.CardCycles.Where(c => c.PaymentMethodId == id).OrderBy(c => c.DueDate).ToListAsync()).Select(CardCycleDto.From));

        cards.MapPut("/{id:int}/cycles", async (int id, List<CardCycleDto> body, FinanzasDbContext db) =>
        {
            db.CardCycles.RemoveRange(db.CardCycles.Where(c => c.PaymentMethodId == id));
            db.CardCycles.AddRange(body.Select(c => new CardCycle { PaymentMethodId = id, ClosingDate = c.ClosingDate, DueDate = c.DueDate }));
            await db.SaveChangesAsync();

            // Con nuevos cierres cambian los vencimientos propuestos de los gastos fijos con esta tarjeta.
            foreach (var rule in await db.RecurringRules.Include(r => r.Amounts).Where(r => r.PaymentMethodId == id).ToListAsync())
                await RecurringRuleSync.RegenerateAsync(db, rule, Today());
            await db.SaveChangesAsync();

            return Results.Ok((await db.CardCycles.Where(c => c.PaymentMethodId == id).OrderBy(c => c.DueDate).ToListAsync()).Select(CardCycleDto.From));
        });
    }

    static async Task<string?> RuleError(FinanzasDbContext db, RecurringRuleDto body)
    {
        if (AmountsError(body) is { } amountsError) return amountsError;
        var type = await db.OperationTypes.FindAsync(body.OperationTypeId);
        if (type is null) return "Elegí un tipo de operación válido.";
        if (type.Kind == OperationKind.PagoTarjeta) return "Los pagos de tarjeta salen de los resúmenes, no se cargan como fijos.";
        var method = await db.PaymentMethods.FindAsync(body.PaymentMethodId);
        if (method is null) return "Elegí un medio de pago válido.";
        if (method.IsCard && type.Kind != OperationKind.Movimiento) return $"{type.Name} sale de una caja, no de una tarjeta.";
        return null;
    }

    /// <summary>Vigencia y cambios de monto: el primero tiene que ser un monto, los incrementos % se aplican sobre él.</summary>
    static string? AmountsError(RecurringRuleDto body)
    {
        if (!Months.TryParse(body.StartMonth, out var start)) return "El mes desde es inválido (AAAA-MM).";
        if (body.EndMonth is not null && (!Months.TryParse(body.EndMonth, out var end) || end < start))
            return "El mes hasta tiene que ser igual o posterior al mes desde.";
        if (body.Amounts.Count == 0) return "El gasto fijo necesita al menos un monto.";
        var months = new HashSet<DateOnly>();
        foreach (var a in body.Amounts)
        {
            if (!Months.TryParse(a.FromMonth, out var m)) return "Cada cambio de monto necesita un mes válido (AAAA-MM).";
            if (!months.Add(m)) return "Hay dos cambios de monto en el mismo mes.";
            if (a.IncreasePercent is { } pct ? pct <= -100 : a.Amount <= 0)
                return a.IncreasePercent is null ? "Cada monto tiene que ser mayor a cero." : "El incremento tiene que ser mayor a -100%.";
        }
        if (body.Amounts.MinBy(a => Months.Parse(a.FromMonth))!.IncreasePercent is not null)
            return "El primer cambio tiene que ser un monto; los incrementos % se aplican sobre él.";
        return null;
    }

    public static void MapRecurringRules(this WebApplication app)
    {
        var rules = app.MapGroup("/recurring-rules");

        rules.MapGet("/", async (string? scope, FinanzasDbContext db) =>
        {
            var sc = ParseScope(scope);
            var list = await db.RecurringRules.Include(r => r.Amounts).Where(r => sc == null || r.Scope == sc).OrderBy(r => r.DayOfMonth).ToListAsync();
            return list.Select(RecurringRuleDto.From);
        });

        rules.MapPost("/", async (RecurringRuleDto body, FinanzasDbContext db) =>
        {
            if (await RuleError(db, body) is { } error) return Results.BadRequest(error);
            var rule = new RecurringRule { Description = body.Description };
            body.ApplyTo(rule);
            db.RecurringRules.Add(rule);
            await db.SaveChangesAsync();
            var missing = await RecurringRuleSync.RegenerateAsync(db, rule, Today());
            await db.SaveChangesAsync();
            return Results.Created($"/recurring-rules/{rule.Id}", new { rule = RecurringRuleDto.From(rule), missingCycles = missing.Select(Months.Format) });
        });

        rules.MapPut("/{id:int}", async (int id, RecurringRuleDto body, FinanzasDbContext db) =>
        {
            var rule = await db.RecurringRules.Include(r => r.Amounts).FirstOrDefaultAsync(r => r.Id == id);
            if (rule is null) return Results.NotFound();
            if (await RuleError(db, body) is { } error) return Results.BadRequest(error);
            db.RecurringRuleAmounts.RemoveRange(rule.Amounts);
            body.ApplyTo(rule);
            var missing = await RecurringRuleSync.RegenerateAsync(db, rule, Today());
            await db.SaveChangesAsync();
            return Results.Ok(new { rule = RecurringRuleDto.From(rule), missingCycles = missing.Select(Months.Format) });
        });

        rules.MapDelete("/{id:int}", async (int id, FinanzasDbContext db) =>
        {
            var rule = await db.RecurringRules.FindAsync(id);
            if (rule is null) return Results.NotFound();
            // Se borran los previstos; los realizados quedan como movimientos sueltos.
            db.Transactions.RemoveRange(db.Transactions.Where(t => t.RecurringRuleId == id && t.Status == TransactionStatus.Previsto));
            await db.Transactions.Where(t => t.RecurringRuleId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.RecurringRuleId, (int?)null));
            db.RecurringRules.Remove(rule);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    public static void MapBudgets(this WebApplication app)
    {
        // El presupuesto es por ámbito: se lee y se guarda el de Familia o el de Memey, nunca mezclados.
        app.MapGet("/budgets/{month}", async (string month, string? scope, FinanzasDbContext db) =>
        {
            if (!Months.TryParse(month, out var first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            var ids = await ScopeCategoryIds(db, ScopeOrFamilia(scope));
            var list = await db.Budgets.Where(b => b.Month == first && ids.Contains(b.CategoryId)).ToListAsync();
            return Results.Ok(list.Select(b => new BudgetDto(month, b.CategoryId, Money.Pesos(b.Amount))));
        });

        // Reemplaza solo el presupuesto del ámbito indicado; el del otro queda como estaba.
        app.MapPut("/budgets/{month}", async (string month, string? scope, List<BudgetDto> body, FinanzasDbContext db) =>
        {
            if (!Months.TryParse(month, out var first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            var sc = ScopeOrFamilia(scope);
            var ids = await ScopeCategoryIds(db, sc);
            if (body.Any(b => !ids.Contains(b.CategoryId)))
                return Results.BadRequest($"Hay categorías que no son de {sc}: el presupuesto de Familia y el de Memey se cargan por separado.");
            db.Budgets.RemoveRange(db.Budgets.Where(b => b.Month == first && ids.Contains(b.CategoryId)));
            db.Budgets.AddRange(body.Where(b => b.Amount > 0).Select(b => new Budget { Month = first, CategoryId = b.CategoryId, Amount = Money.Cents(b.Amount) }));
            await db.SaveChangesAsync();
            var list = await db.Budgets.Where(b => b.Month == first && ids.Contains(b.CategoryId)).ToListAsync();
            return Results.Ok(list.Select(b => new BudgetDto(month, b.CategoryId, Money.Pesos(b.Amount))));
        });

        app.MapGet("/reports/budget-vs-actual", async (string? month, DateOnly? cutoff, string? scope, FinanzasDbContext db) =>
        {
            var cut = cutoff ?? Today();
            var first = new DateOnly(cut.Year, cut.Month, 1);
            if (month is not null && !Months.TryParse(month, out first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            var sc = ScopeOrFamilia(scope);

            var rows = Budgeting.Report(
                await db.Categories.ToListAsync(), await db.Budgets.ToListAsync(), await db.Transactions.ToListAsync(),
                sc, first, cut, await db.Settings.FirstAsync());

            var dto = rows.Select(r => new BudgetVsActualRowDto(
                r.Category.Id, r.Category.Name, r.Category.BudgetType, Money.Pesos(r.Budget), Money.Pesos(r.Actual),
                Money.Pesos(r.Available), r.UsePct, Money.Pesos(r.Projected), r.Alert)).ToList();
            return Results.Ok(new BudgetVsActualDto(Months.Format(first), cut, sc, dto,
                dto.Sum(r => r.Budget), dto.Sum(r => r.Actual), dto.Sum(r => r.Projected)));
        });

        // Detalle de una fila del reporte: los movimientos que suman el real de la categoría en el mes al corte.
        app.MapGet("/reports/budget-vs-actual/{categoryId:int}/transactions", async (int categoryId, string? month, DateOnly? cutoff, FinanzasDbContext db) =>
        {
            var cut = cutoff ?? Today();
            var first = new DateOnly(cut.Year, cut.Month, 1);
            if (month is not null && !Months.TryParse(month, out first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            if (await db.Categories.FindAsync(categoryId) is not { } category) return Results.NotFound();

            var candidates = await db.Transactions.Where(t => t.CategoryId == categoryId).ToListAsync();
            var list = Budgeting.ActualTransactions(candidates, category.Scope, first, cut)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .Select(TransactionDto.From);
            return Results.Ok(list);
        });
    }

    static Task<List<int>> ScopeCategoryIds(FinanzasDbContext db, Scope scope) =>
        db.Categories.Where(c => c.Scope == scope).Select(c => c.Id).ToListAsync();

    public static void MapSettings(this WebApplication app)
    {
        static async Task<SettingsDto> Load(FinanzasDbContext db)
        {
            var s = await db.Settings.FirstAsync();
            return new SettingsDto((await db.CashBoxes.OrderBy(b => b.Id).ToListAsync()).Select(CashBoxDto.From).ToList(),
                Money.Pesos(s.CashFloor), Money.Pesos(s.SavingsGoal), s.AlertAttentionPct, s.AlertControlPct, s.AlertExceededPct, s.AlertCriticalPct);
        }

        app.MapGet("/settings", Load);

        app.MapPut("/settings", async (UpdateSettingsDto body, FinanzasDbContext db) =>
        {
            var s = await db.Settings.FirstAsync();
            s.CashFloor = Money.Cents(body.CashFloor);
            s.SavingsGoal = Money.Cents(body.SavingsGoal);
            s.AlertAttentionPct = body.AlertAttentionPct;
            s.AlertControlPct = body.AlertControlPct;
            s.AlertExceededPct = body.AlertExceededPct;
            s.AlertCriticalPct = body.AlertCriticalPct;
            await db.SaveChangesAsync();
            return await Load(db);
        });

        app.MapPut("/settings/cash-boxes/{id:int}", async (int id, CashBoxDto input, FinanzasDbContext db) =>
        {
            var box = await db.CashBoxes.FindAsync(id);
            if (box is null) return Results.NotFound();
            box.OpeningBalance = Money.Cents(input.OpeningBalance);
            box.OpeningDate = input.OpeningDate;
            box.ObservedBalance = input.ObservedBalance is { } o ? Money.Cents(o) : null;
            box.ObservedDate = input.ObservedDate;
            await db.SaveChangesAsync();
            return Results.Ok(CashBoxDto.From(box));
        });

        app.MapGet("/people", async (FinanzasDbContext db) => await db.People.OrderBy(p => p.Id).ToListAsync());
    }

    public static void MapImport(this WebApplication app) =>
        app.MapPost("/import/sheet", async (SheetSnapshot snapshot, bool? replace, FinanzasDbContext db) =>
        {
            try
            {
                return Results.Ok(await SheetImport.ApplyAsync(db, snapshot, replace ?? false, Today()));
            }
            catch (InvalidOperationException e)
            {
                return Results.Conflict(e.Message);
            }
        });
}
