using Finanzas.Domain;
using Finanzas.Infrastructure;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FinanzasDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Finanzas") ?? "Data Source=finanzas.db"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<FinanzasDbContext>().Database.Migrate();

app.UseCors();
app.MapOpenApi();

// La fecha de corte no se guarda: si no viene, es hoy.
static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
static Scope? ParseScope(string? s) => s?.ToLowerInvariant() switch { "familia" => Scope.Familia, "memey" => Scope.Memey, _ => null };
static DateOnly EndOfMonth(DateOnly d) => new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/dashboard", async (string? scope, DateOnly? date, FinanzasDbContext db) =>
{
    var cutoff = date ?? Today();
    var sc = ParseScope(scope);
    var calc = new CashFlowCalculator(
        await db.CashBoxes.ToListAsync(),
        await db.Transactions.Include(t => t.Impacts).ToListAsync(),
        await db.Statements.ToListAsync());

    var quarter = Enumerable.Range(0, 3)
        .Select(k => EndOfMonth(cutoff.AddMonths(k)))
        .Select(d => new { month = d.ToString("yyyy-MM"), balance = calc.ProjectedAt(sc, cutoff, d) / 100m });

    return Results.Ok(new
    {
        cutoff,
        cashNow = calc.CashAt(sc, cutoff) / 100m,
        projectedEndOfMonth = calc.ProjectedAt(sc, cutoff, EndOfMonth(cutoff)) / 100m,
        quarter,
        memey = new
        {
            cashNow = calc.CashAt(Scope.Memey, cutoff) / 100m,
            projectedEndOfMonth = calc.ProjectedAt(Scope.Memey, cutoff, EndOfMonth(cutoff)) / 100m,
        },
        cardPayments = calc.CardStatements(sc, cutoff, EndOfMonth(cutoff.AddMonths(5)))
            .Where(s => s.Outstanding > 0)
            .Select(s => new { s.CardId, s.DueDate, total = s.Outstanding / 100m }),
    });
});

var tx = app.MapGroup("/transactions");

tx.MapGet("/", async (DateOnly? from, DateOnly? to, FinanzasDbContext db) =>
    await db.Transactions
        .Where(t => (from == null || t.Date >= from) && (to == null || t.Date <= to))
        .OrderByDescending(t => t.Date).ToListAsync());

tx.MapPost("/", async (Transaction input, FinanzasDbContext db) =>
{
    var method = await db.PaymentMethods.Include(m => m.Cycles).FirstOrDefaultAsync(m => m.Id == input.PaymentMethodId);
    if (method is null) return Results.BadRequest("Cada movimiento necesita un medio de pago válido.");

    if (method.IsCard && input.Operation is not Operation.PagoTarjeta)
    {
        input.FirstDueDate ??= CardCalendar.ProposeDueDate(method.Cycles, input.Date);
        if (input.FirstDueDate is null)
            return Results.BadRequest("Falta cargar el cierre de la tarjeta para esa fecha en Configuración.");
    }

    var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
    input.Impacts = CashImpactBuilder.Build(input, method, s => boxes[s]);
    db.Transactions.Add(input);
    await db.SaveChangesAsync();
    return Results.Created($"/transactions/{input.Id}", input);
});

tx.MapPost("/{id:int}/confirm", async (int id, ConfirmRequest req, FinanzasDbContext db) =>
{
    var t = await db.Transactions.Include(x => x.Impacts).FirstOrDefaultAsync(x => x.Id == id);
    if (t is null) return Results.NotFound();
    var method = await db.PaymentMethods.FindAsync(t.PaymentMethodId);
    var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);

    t.Status = TransactionStatus.Realizado;
    t.Date = req.Date ?? Today();
    t.Amount = req.Amount ?? t.Amount;
    db.CashImpacts.RemoveRange(t.Impacts);
    t.Impacts = CashImpactBuilder.Build(t, method!, s => boxes[s]);
    await db.SaveChangesAsync();
    return Results.Ok(t);
});

var cards = app.MapGroup("/cards");

cards.MapGet("/{id:int}/due-date", async (int id, DateOnly purchaseDate, FinanzasDbContext db) =>
{
    var cycles = await db.CardCycles.Where(c => c.PaymentMethodId == id).ToListAsync();
    var due = CardCalendar.ProposeDueDate(cycles, purchaseDate);
    return due is null ? Results.NotFound("No hay cierre cargado para esa fecha.") : Results.Ok(new { firstDueDate = due });
});

cards.MapGet("/{id:int}/cycles", async (int id, FinanzasDbContext db) =>
    await db.CardCycles.Where(c => c.PaymentMethodId == id).OrderBy(c => c.DueDate).ToListAsync());

cards.MapPut("/{id:int}/cycles", async (int id, List<CardCycle> cycles, FinanzasDbContext db) =>
{
    db.CardCycles.RemoveRange(db.CardCycles.Where(c => c.PaymentMethodId == id));
    foreach (var c in cycles) { c.Id = 0; c.PaymentMethodId = id; }
    db.CardCycles.AddRange(cycles);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapGet("/settings", async (FinanzasDbContext db) =>
{
    var settings = await db.Settings.FirstAsync();
    return new { cashBoxes = await db.CashBoxes.ToListAsync(), cashFloor = settings.CashFloor / 100m, settings };
});

app.MapGet("/people", async (FinanzasDbContext db) => await db.People.ToListAsync());
app.MapGet("/categories", async (FinanzasDbContext db) => await db.Categories.OrderBy(c => c.Name).ToListAsync());
app.MapGet("/payment-methods", async (FinanzasDbContext db) => await db.PaymentMethods.ToListAsync());

app.MapPut("/settings/cash-boxes/{id:int}", async (int id, CashBox input, FinanzasDbContext db) =>
{
    var box = await db.CashBoxes.FindAsync(id);
    if (box is null) return Results.NotFound();
    box.OpeningBalance = input.OpeningBalance;
    box.OpeningDate = input.OpeningDate;
    box.ObservedBalance = input.ObservedBalance;
    box.ObservedDate = input.ObservedDate;
    await db.SaveChangesAsync();
    return Results.Ok(box);
});

app.Run();

record ConfirmRequest(DateOnly? Date, long? Amount);

public partial class Program;
