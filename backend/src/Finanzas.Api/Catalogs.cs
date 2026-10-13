using Finanzas.Domain;
using Finanzas.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Api;

/// <summary>
/// Configuración de tipos de operación, categorías y medios de pago. Lo que está en uso no se borra: se desactiva
/// (deja de ofrecerse al cargar y los movimientos existentes lo conservan).
/// </summary>
public static class Catalogs
{
    static IResult InUse(string what) => Results.Conflict($"{what} está en uso. Desactivalo para que no se ofrezca más.");

    static string? NameError(string? name) => string.IsNullOrWhiteSpace(name) ? "Poné un nombre." : null;

    public static void MapOperationTypes(this WebApplication app)
    {
        var g = app.MapGroup("/operation-types");

        g.MapGet("/", async (string? scope, FinanzasDbContext db) =>
        {
            var sc = Endpoints.ParseScope(scope);
            var list = await db.OperationTypes.OrderBy(t => t.Id).ToListAsync();
            return list.Where(t => sc is null || t.AppliesTo(sc.Value)).Select(OperationTypeDto.From);
        });

        g.MapPost("/", async (OperationTypeDto body, FinanzasDbContext db) =>
        {
            var error = Error(body) ?? (body.Kind == OperationKind.PagoTarjeta ? "El pago de tarjeta ya existe; no se crea otro." : null);
            if (error is not null) return Results.BadRequest(error);
            if (await db.OperationTypes.AnyAsync(t => t.Name == body.Name.Trim())) return Results.Conflict("Ya hay un tipo con ese nombre.");
            var type = new OperationType { Name = body.Name.Trim() };
            Apply(body, type);
            db.OperationTypes.Add(type);
            await db.SaveChangesAsync();
            return Results.Created($"/operation-types/{type.Id}", OperationTypeDto.From(type));
        });

        g.MapPut("/{id:int}", async (int id, OperationTypeDto body, FinanzasDbContext db) =>
        {
            var type = await db.OperationTypes.FindAsync(id);
            if (type is null) return Results.NotFound();
            if (Error(body) is { } error) return Results.BadRequest(error);
            if (await db.OperationTypes.AnyAsync(t => t.Id != id && t.Name == body.Name.Trim())) return Results.Conflict("Ya hay un tipo con ese nombre.");

            var used = await db.Transactions.AnyAsync(t => t.OperationTypeId == id) || await db.RecurringRules.AnyAsync(r => r.OperationTypeId == id);
            // Pasar de/a pago de tarjeta o transferencia cambia qué datos necesita el movimiento: no se hace sobre datos cargados.
            var structural = new[] { OperationKind.PagoTarjeta, OperationKind.Transferencia };
            if (used && body.Kind != type.Kind && (structural.Contains(body.Kind) || structural.Contains(type.Kind)))
                return Results.Conflict("Este tipo ya tiene movimientos: no puede pasar a (o dejar de ser) pago de tarjeta o transferencia. Creá uno nuevo.");
            if (body.Kind is OperationKind.Ahorro or OperationKind.Inversion && type.Kind == OperationKind.Movimiento)
            {
                var cards = db.PaymentMethods.Where(m => m.Type == PaymentMethodType.Tarjeta).Select(m => m.Id);
                if (await db.Transactions.AnyAsync(t => t.OperationTypeId == id && cards.Contains(t.PaymentMethodId))
                    || await db.RecurringRules.AnyAsync(r => r.OperationTypeId == id && cards.Contains(r.PaymentMethodId)))
                    return Results.Conflict("Este tipo tiene movimientos con tarjeta; ahorro e inversión salen de una caja. Creá un tipo nuevo.");
            }
            if (type.Kind == OperationKind.PagoTarjeta && body.Kind != OperationKind.PagoTarjeta)
                return Results.Conflict("El pago de tarjeta no cambia de comportamiento; podés renombrarlo o elegir sus ámbitos.");

            var cashChanged = body.Effect != type.Effect || body.TransferTo != type.TransferTo || body.Kind != type.Kind;
            type.Name = body.Name.Trim();
            Apply(body, type);
            if (cashChanged)
                await RebuildImpactsAsync(db, await db.Transactions.Include(t => t.Impacts).Where(t => t.OperationTypeId == id).ToListAsync());
            await db.SaveChangesAsync();
            return Results.Ok(OperationTypeDto.From(type));
        });

        g.MapDelete("/{id:int}", async (int id, FinanzasDbContext db) =>
        {
            var type = await db.OperationTypes.FindAsync(id);
            if (type is null) return Results.NotFound();
            if (type.Kind == OperationKind.PagoTarjeta) return Results.Conflict("El pago de tarjeta no se borra.");
            if (await db.Transactions.AnyAsync(t => t.OperationTypeId == id) || await db.RecurringRules.AnyAsync(r => r.OperationTypeId == id))
                return InUse("El tipo");
            db.OperationTypes.Remove(type);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        static string? Error(OperationTypeDto b) =>
            NameError(b.Name)
            ?? (b.Scopes is null || b.Scopes.Count == 0 ? "Elegí al menos un ámbito (Familia o Memey)." : null)
            ?? (b.Kind == OperationKind.Transferencia && b.TransferTo is null ? "Indicá a qué caja va la transferencia." : null);

        static void Apply(OperationTypeDto b, OperationType t)
        {
            t.Kind = b.Kind;
            t.Effect = b.Kind == OperationKind.PagoTarjeta ? CashEffect.Resta : b.Effect;
            t.ForFamilia = b.Scopes.Contains(Scope.Familia);
            t.ForMemey = b.Scopes.Contains(Scope.Memey);
            t.TransferTo = b.Kind == OperationKind.Transferencia ? b.TransferTo : null;
            t.Active = b.Active;
        }
    }

    public static void MapCategories(this WebApplication app)
    {
        var g = app.MapGroup("/categories");

        g.MapGet("/", async (string? scope, FinanzasDbContext db) =>
        {
            var sc = Endpoints.ParseScope(scope);
            var list = await db.Categories.Where(c => sc == null || c.Scope == sc).OrderBy(c => c.Name).ToListAsync();
            return list.Select(CategoryDto.From);
        });

        g.MapPost("/", async (CategoryDto body, FinanzasDbContext db) =>
        {
            if (NameError(body.Name) is { } error) return Results.BadRequest(error);
            if (await db.Categories.AnyAsync(c => c.Scope == body.Scope && c.Name == body.Name.Trim()))
                return Results.Conflict("Ya hay una categoría con ese nombre en ese ámbito.");
            var c = new Category { Name = body.Name.Trim(), Scope = body.Scope, Kind = body.Kind, BudgetType = body.BudgetType, Active = body.Active };
            db.Categories.Add(c);
            await db.SaveChangesAsync();
            return Results.Created($"/categories/{c.Id}", CategoryDto.From(c));
        });

        g.MapPut("/{id:int}", async (int id, CategoryDto body, FinanzasDbContext db) =>
        {
            var c = await db.Categories.FindAsync(id);
            if (c is null) return Results.NotFound();
            if (NameError(body.Name) is { } error) return Results.BadRequest(error);
            if (await db.Categories.AnyAsync(x => x.Id != id && x.Scope == body.Scope && x.Name == body.Name.Trim()))
                return Results.Conflict("Ya hay una categoría con ese nombre en ese ámbito.");
            if (body.Scope != c.Scope && await Used(db, id))
                return Results.Conflict("La categoría ya tiene movimientos: no cambia de ámbito. Creá una nueva en el otro.");
            c.Name = body.Name.Trim(); c.Scope = body.Scope; c.Kind = body.Kind; c.BudgetType = body.BudgetType; c.Active = body.Active;
            await db.SaveChangesAsync();
            return Results.Ok(CategoryDto.From(c));
        });

        g.MapDelete("/{id:int}", async (int id, FinanzasDbContext db) =>
        {
            var c = await db.Categories.FindAsync(id);
            if (c is null) return Results.NotFound();
            if (await Used(db, id)) return InUse("La categoría");
            db.Categories.Remove(c);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        static async Task<bool> Used(FinanzasDbContext db, int id) =>
            await db.Transactions.AnyAsync(t => t.CategoryId == id) || await db.RecurringRules.AnyAsync(r => r.CategoryId == id)
            || await db.Budgets.AnyAsync(b => b.CategoryId == id);
    }

    public static void MapPaymentMethods(this WebApplication app)
    {
        var g = app.MapGroup("/payment-methods");

        g.MapGet("/", async (FinanzasDbContext db) =>
            (await db.PaymentMethods.OrderBy(m => m.Id).ToListAsync()).Select(PaymentMethodDto.From));

        g.MapPost("/", async (PaymentMethodDto body, FinanzasDbContext db) =>
        {
            if (NameError(body.Name) is { } error) return Results.BadRequest(error);
            if (!await db.CashBoxes.AnyAsync(b => b.Id == body.CashBoxId)) return Results.BadRequest("Elegí la caja del medio de pago.");
            if (await db.PaymentMethods.AnyAsync(m => m.Name == body.Name.Trim())) return Results.Conflict("Ya hay un medio de pago con ese nombre.");
            var m = new PaymentMethod { Name = body.Name.Trim(), Type = body.Type, CashBoxId = body.CashBoxId, Active = body.Active };
            db.PaymentMethods.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/payment-methods/{m.Id}", PaymentMethodDto.From(m));
        });

        g.MapPut("/{id:int}", async (int id, PaymentMethodDto body, FinanzasDbContext db) =>
        {
            var m = await db.PaymentMethods.FindAsync(id);
            if (m is null) return Results.NotFound();
            if (NameError(body.Name) is { } error) return Results.BadRequest(error);
            if (!await db.CashBoxes.AnyAsync(b => b.Id == body.CashBoxId)) return Results.BadRequest("Elegí la caja del medio de pago.");
            if (await db.PaymentMethods.AnyAsync(x => x.Id != id && x.Name == body.Name.Trim())) return Results.Conflict("Ya hay un medio de pago con ese nombre.");
            // Una tarjeta mueve la caja en el vencimiento y el resto en el día: cambiar eso reescribiría la historia.
            if ((body.Type == PaymentMethodType.Tarjeta) != m.IsCard && await Used(db, id))
                return Results.Conflict("El medio ya tiene movimientos: no puede pasar a ser tarjeta (o dejar de serlo). Creá uno nuevo.");
            m.Name = body.Name.Trim(); m.Type = body.Type; m.CashBoxId = body.CashBoxId; m.Active = body.Active;
            await db.SaveChangesAsync();
            return Results.Ok(PaymentMethodDto.From(m));
        });

        g.MapDelete("/{id:int}", async (int id, FinanzasDbContext db) =>
        {
            var m = await db.PaymentMethods.FindAsync(id);
            if (m is null) return Results.NotFound();
            if (await Used(db, id)) return InUse("El medio de pago");
            db.CardCycles.RemoveRange(db.CardCycles.Where(c => c.PaymentMethodId == id));
            db.PaymentMethods.Remove(m);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        static async Task<bool> Used(FinanzasDbContext db, int id) =>
            await db.Transactions.AnyAsync(t => t.PaymentMethodId == id || t.PaidCardId == id)
            || await db.RecurringRules.AnyAsync(r => r.PaymentMethodId == id)
            || await db.Statements.AnyAsync(s => s.PaymentMethodId == id);
    }

    /// <summary>Ahorro e inversión del ámbito, aparte de la caja. El valor de mercado viene del proveedor de inversiones.</summary>
    public static void MapSavings(this WebApplication app)
    {
        app.MapGet("/savings", async (string? scope, DateOnly? date, FinanzasDbContext db, IInvestmentProvider provider) =>
        {
            var sc = Endpoints.ScopeOrFamilia(scope);
            var cutoff = date ?? Endpoints.Today();
            var transactions = await db.Transactions.Where(t => t.Scope == sc).ToListAsync();

            List<SavingsMovementDto> Movements(OperationKind kind) => Savings.Movements(transactions, sc, kind)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .Select(t => new SavingsMovementDto(t.Id, t.Date, t.OperationTypeId, t.Description, Money.Pesos(-t.OperationType.Sign * t.Amount), t.Status))
                .ToList();

            var valuation = await db.InvestmentValuations.Where(v => v.Scope == sc && v.Date <= cutoff)
                .OrderByDescending(v => v.Date).ThenByDescending(v => v.Id).FirstOrDefaultAsync();

            return Results.Ok(new SavingsDto(sc, cutoff,
                new SavingsBucketDto(Money.Pesos(Savings.BalanceAt(transactions, sc, OperationKind.Ahorro, cutoff)), Movements(OperationKind.Ahorro)),
                new InvestmentsDto(Money.Pesos(Savings.BalanceAt(transactions, sc, OperationKind.Inversion, cutoff)),
                    valuation is null ? null : Money.Pesos(valuation.MarketValue), valuation?.Date,
                    provider.Name, provider.IsConnected, Movements(OperationKind.Inversion))));
        });

        // Trae el valor de mercado del broker y lo guarda como valuación del día. Sin proveedor conectado responde 503.
        app.MapPost("/investments/sync", async (string? scope, FinanzasDbContext db, IInvestmentProvider provider, CancellationToken ct) =>
        {
            if (!provider.IsConnected) return Results.Problem("Invertir Online todavía no está conectado.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var sc = Endpoints.ScopeOrFamilia(scope);
            var portfolio = await provider.GetPortfolioAsync(sc, ct);
            if (portfolio is null) return Results.NotFound($"{provider.Name} no tiene cuenta para {sc}.");
            db.InvestmentValuations.Add(new InvestmentValuation { Scope = sc, Date = portfolio.Date, MarketValue = portfolio.MarketValue, Source = provider.Name });
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                scope = sc, date = portfolio.Date, marketValue = Money.Pesos(portfolio.MarketValue),
                positions = portfolio.Positions.Select(p => new { p.Symbol, p.Description, p.Quantity, marketValue = Money.Pesos(p.MarketValue) }),
            });
        });
    }

    static async Task RebuildImpactsAsync(FinanzasDbContext db, List<Transaction> transactions)
    {
        var methods = await db.PaymentMethods.Include(m => m.Cycles).ToDictionaryAsync(m => m.Id);
        var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
        foreach (var t in transactions)
        {
            db.CashImpacts.RemoveRange(t.Impacts);
            t.Impacts = CashImpactBuilder.Build(t, methods[t.PaymentMethodId], s => boxes[s]);
        }
    }
}
