using Finanzas.Domain;
using Finanzas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Api;

public static class Endpoints
{
    static readonly System.Globalization.CultureInfo EsAr = new("es-AR");

    // La fecha de corte no se guarda: si no viene, es hoy.
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>"familia" o "memey"; "total" o vacío es la suma de ambos (null).</summary>
    public static Scope? ParseScope(string? s) => s?.ToLowerInvariant() switch { "familia" => Scope.Familia, "memey" => Scope.Memey, _ => null };

    public static void MapDashboard(this WebApplication app) => app.MapGet("/dashboard", async (string? scope, DateOnly? date, FinanzasDbContext db) =>
    {
        var cutoff = date ?? Today();
        var sc = ParseScope(scope);
        var settings = await db.Settings.FirstAsync();
        var categories = await db.Categories.ToListAsync();
        var budgets = await db.Budgets.ToListAsync();
        var rules = await db.RecurringRules.Include(r => r.Amounts).ToListAsync();
        var transactions = await db.Transactions.Include(t => t.Impacts).ToListAsync();
        var cards = await db.PaymentMethods.Where(m => m.Type == PaymentMethodType.Tarjeta).ToDictionaryAsync(m => m.Id, m => m.Name);

        var horizon = Months.EndOf(cutoff.AddMonths(5));
        var estimates = Budgeting.MonthlyEstimates(categories, budgets, rules, Scope.Familia, cutoff, horizon);
        var calc = new CashFlowCalculator(await db.CashBoxes.ToListAsync(), transactions, await db.Statements.ToListAsync(), estimates);

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

        var alerts = new List<string>();
        foreach (var q in quarter.Where(q => q.balance < settings.CashFloor))
            alerts.Add(string.Create(EsAr, $"La caja proyectada a fin de {q.month} (${Money.Pesos(q.balance):N0}) queda debajo del piso de ${Money.Pesos(settings.CashFloor):N0}."));
        if (sc is not Scope.Memey)
            alerts.AddRange(Budgeting.Report(categories, budgets, transactions, Scope.Familia, cutoff, cutoff, settings)
                .Where(r => r.Alert != "Normal")
                .Select(r => string.Create(EsAr, $"{r.Category.Name}: {r.Alert} ({r.UsePct:0.#}% usado)")));

        return Results.Ok(new
        {
            cutoff,
            scope = sc?.ToString().ToLowerInvariant() ?? "total",
            cashNow = Money.Pesos(calc.CashAt(sc, cutoff)),
            projectedEndOfMonth = Money.Pesos(projectedEom),
            freeCash = Money.Pesos(projectedEom - calc.CommittedCardInstallmentsAfter(sc, endOfMonth)),
            quarter = quarter.Select(q => new { q.month, balance = Money.Pesos(q.balance) }),
            estimatedMonthlySpend = estimates.Select(e => new { month = Months.Format(e.Date), amount = Money.Pesos(e.Amount) }),
            memey = new
            {
                cashNow = Money.Pesos(calc.CashAt(Scope.Memey, cutoff)),
                projectedEndOfMonth = Money.Pesos(calc.ProjectedAt(Scope.Memey, cutoff, endOfMonth)),
            },
            cardPayments,
            alerts,
        });
    });

    public static void MapTransactions(this WebApplication app)
    {
        var tx = app.MapGroup("/transactions");

        tx.MapGet("/", async (string? scope, DateOnly? from, DateOnly? to, TransactionStatus? status, FinanzasDbContext db) =>
        {
            var sc = ParseScope(scope);
            var list = await db.Transactions
                .Where(t => (sc == null || t.Scope == sc) && (from == null || t.Date >= from) && (to == null || t.Date <= to) && (status == null || t.Status == status))
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();
            return list.Select(TransactionDto.From);
        });

        tx.MapPost("/", async (NewTransactionDto body, FinanzasDbContext db) =>
        {
            var input = body.ToEntity();
            var method = await db.PaymentMethods.Include(m => m.Cycles).FirstOrDefaultAsync(m => m.Id == input.PaymentMethodId);
            if (method is null) return Results.BadRequest("Cada movimiento necesita un medio de pago válido.");

            if (method.IsCard && input.Operation is not Operation.PagoTarjeta)
            {
                input.FirstDueDate ??= CardCalendar.ProposeDueDate(method.Cycles, input.Date);
                if (input.FirstDueDate is null)
                    return Results.BadRequest("Falta cargar el cierre de la tarjeta para esa fecha en Configuración.");
            }
            if (input.Operation is Operation.PagoTarjeta && (input.PaidCardId is null || input.FirstDueDate is null))
                return Results.BadRequest("Un pago de tarjeta necesita la tarjeta y el vencimiento que paga.");

            var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
            input.Impacts = CashImpactBuilder.Build(input, method, s => boxes[s]);
            db.Transactions.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/transactions/{input.Id}", TransactionDto.From(input));
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
            if (method.IsCard && t.Operation != Operation.PagoTarjeta && t.RecurringRuleId is not null)
                t.FirstDueDate = CardCalendar.ProposeDueDate(method.Cycles, t.Date) ?? t.FirstDueDate;
            db.CashImpacts.RemoveRange(t.Impacts);
            t.Impacts = CashImpactBuilder.Build(t, method, s => boxes[s]);
            await db.SaveChangesAsync();
            return Results.Ok(TransactionDto.From(t));
        });

        tx.MapDelete("/{id:int}", async (int id, FinanzasDbContext db) =>
        {
            var t = await db.Transactions.FindAsync(id);
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
            if (body.Amounts.Count == 0) return Results.BadRequest("El gasto fijo necesita al menos un monto.");
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
            if (body.Amounts.Count == 0) return Results.BadRequest("El gasto fijo necesita al menos un monto.");
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
        app.MapGet("/budgets/{month}", async (string month, FinanzasDbContext db) =>
        {
            if (!Months.TryParse(month, out var first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            var list = await db.Budgets.Where(b => b.Month == first).ToListAsync();
            return Results.Ok(list.Select(b => new BudgetDto(month, b.CategoryId, Money.Pesos(b.Amount))));
        });

        app.MapPut("/budgets/{month}", async (string month, List<BudgetDto> body, FinanzasDbContext db) =>
        {
            if (!Months.TryParse(month, out var first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            db.Budgets.RemoveRange(db.Budgets.Where(b => b.Month == first));
            db.Budgets.AddRange(body.Where(b => b.Amount > 0).Select(b => new Budget { Month = first, CategoryId = b.CategoryId, Amount = Money.Cents(b.Amount) }));
            await db.SaveChangesAsync();
            var list = await db.Budgets.Where(b => b.Month == first).ToListAsync();
            return Results.Ok(list.Select(b => new BudgetDto(month, b.CategoryId, Money.Pesos(b.Amount))));
        });

        app.MapGet("/reports/budget-vs-actual", async (string? month, DateOnly? cutoff, string? scope, FinanzasDbContext db) =>
        {
            var cut = cutoff ?? Today();
            var first = new DateOnly(cut.Year, cut.Month, 1);
            if (month is not null && !Months.TryParse(month, out first)) return Results.BadRequest("Mes con formato yyyy-MM.");
            var sc = ParseScope(scope) ?? Scope.Familia;

            var rows = Budgeting.Report(
                await db.Categories.ToListAsync(), await db.Budgets.ToListAsync(), await db.Transactions.ToListAsync(),
                sc, first, cut, await db.Settings.FirstAsync());

            var dto = rows.Select(r => new BudgetVsActualRowDto(
                r.Category.Id, r.Category.Name, r.Category.BudgetType, Money.Pesos(r.Budget), Money.Pesos(r.Actual),
                Money.Pesos(r.Available), r.UsePct, Money.Pesos(r.Projected), r.Alert)).ToList();
            return Results.Ok(new BudgetVsActualDto(Months.Format(first), cut, sc, dto,
                dto.Sum(r => r.Budget), dto.Sum(r => r.Actual), dto.Sum(r => r.Projected)));
        });
    }

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
        app.MapGet("/categories", async (string? scope, FinanzasDbContext db) =>
        {
            var sc = ParseScope(scope);
            return await db.Categories.Where(c => sc == null || c.Scope == sc).OrderBy(c => c.Name).ToListAsync();
        });
        app.MapGet("/payment-methods", async (FinanzasDbContext db) =>
            (await db.PaymentMethods.OrderBy(m => m.Id).ToListAsync()).Select(PaymentMethodDto.From));
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
