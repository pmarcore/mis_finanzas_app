using Finanzas.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Infrastructure;

/// <summary>
/// Foto de la planilla "Finanzas familiares y Memey" lista para cargar. Montos en pesos; personas, categorías,
/// medios y tarjetas por nombre. Ver data/import/README.md.
/// </summary>
public record SheetSnapshot(
    string Source,
    DateOnly OpeningDate,
    decimal OpeningFamilia,
    decimal OpeningMemey,
    List<SnapshotCycle> CardCycles,
    List<SnapshotCategory> Categories,
    List<SnapshotBudget> Budgets,
    List<SnapshotRule> Rules,
    List<SnapshotTransaction> Transactions);

public record SnapshotCycle(string Card, DateOnly ClosingDate, DateOnly DueDate);
public record SnapshotCategory(string Name, Scope Scope, CategoryKind Kind, BudgetType BudgetType);
public record SnapshotBudget(string Month, string Category, decimal Amount);
public record SnapshotRule(string Key, Scope Scope, string Person, Operation Operation, string Description, string Category,
    string PaymentMethod, int DayOfMonth, string StartMonth, decimal Amount);
public record SnapshotTransaction(string ImportId, DateOnly Date, Scope Scope, string Person, Operation Operation, string Description,
    string? Category, decimal Amount, string PaymentMethod, int Installments, DateOnly? FirstDueDate, TransactionStatus Status,
    string? PaidCard = null, string? Rule = null, bool PriorCommitment = false);

public record ImportResult(int Transactions, int Rules, int Budgets, int Categories, int CardCycles, List<string> Warnings);

public static class SheetImport
{
    public static long Cents(decimal pesos) => (long)Math.Round(pesos * 100, MidpointRounding.AwayFromZero);
    public static DateOnly ParseMonth(string month) => DateOnly.ParseExact(month + "-01", "yyyy-MM-dd");

    /// <summary>
    /// Carga la foto sobre una base sin movimientos. Con replace=true borra antes movimientos, fijos, presupuestos,
    /// categorías y ciclos de tarjeta (las personas, medios de pago y configuración se conservan).
    /// </summary>
    public static async Task<ImportResult> ApplyAsync(FinanzasDbContext db, SheetSnapshot snap, bool replace, DateOnly today)
    {
        if (await db.Transactions.AnyAsync())
        {
            if (!replace) throw new InvalidOperationException("La base ya tiene movimientos. Usá replace=true para reemplazarlos con la planilla.");
            db.Transactions.RemoveRange(db.Transactions);
            db.Statements.RemoveRange(db.Statements);
            db.RecurringRules.RemoveRange(db.RecurringRules);
            db.Budgets.RemoveRange(db.Budgets);
            db.CardCycles.RemoveRange(db.CardCycles);
            await db.SaveChangesAsync();
            db.Categories.RemoveRange(db.Categories);
            await db.SaveChangesAsync();
        }

        var warnings = new List<string>();
        var people = await db.People.ToDictionaryAsync(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var methods = await db.PaymentMethods.ToDictionaryAsync(m => m.Name, StringComparer.OrdinalIgnoreCase);
        int Person(string name) => people.TryGetValue(name, out var p) ? p.Id : throw new InvalidOperationException($"Persona desconocida: {name}");
        PaymentMethod Method(string name) => methods.TryGetValue(name, out var m) ? m : throw new InvalidOperationException($"Medio de pago desconocido: {name}");

        foreach (var box in await db.CashBoxes.ToListAsync())
        {
            box.OpeningDate = snap.OpeningDate;
            box.OpeningBalance = Cents(box.Scope == Scope.Familia ? snap.OpeningFamilia : snap.OpeningMemey);
        }

        foreach (var c in snap.CardCycles)
            db.CardCycles.Add(new CardCycle { PaymentMethodId = Method(c.Card).Id, ClosingDate = c.ClosingDate, DueDate = c.DueDate });

        var categories = snap.Categories.Select(c => new Category { Name = c.Name, Scope = c.Scope, Kind = c.Kind, BudgetType = c.BudgetType }).ToList();
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
        int Category(Scope scope, string name) =>
            categories.FirstOrDefault(c => c.Scope == scope && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
            ?? throw new InvalidOperationException($"Categoría desconocida en {scope}: {name}");

        foreach (var b in snap.Budgets)
            db.Budgets.Add(new Budget { Month = ParseMonth(b.Month), CategoryId = Category(Scope.Familia, b.Category), Amount = Cents(b.Amount) });

        var rules = snap.Rules.ToDictionary(r => r.Key, r => new RecurringRule
        {
            Scope = r.Scope, PersonId = Person(r.Person), Operation = r.Operation, Description = r.Description,
            CategoryId = Category(r.Scope, r.Category), PaymentMethodId = Method(r.PaymentMethod).Id, DayOfMonth = r.DayOfMonth,
            StartMonth = ParseMonth(r.StartMonth),
            Amounts = [new RecurringRuleAmount { FromMonth = ParseMonth(r.StartMonth), Amount = Cents(r.Amount) }],
        });
        db.RecurringRules.AddRange(rules.Values);
        await db.SaveChangesAsync();

        var cycles = await db.CardCycles.ToListAsync();
        var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
        foreach (var s in snap.Transactions)
        {
            var method = Method(s.PaymentMethod);
            method.Cycles = cycles.Where(c => c.PaymentMethodId == method.Id).ToList();
            var t = new Transaction
            {
                ImportId = s.ImportId, Scope = s.Scope, PersonId = Person(s.Person), Operation = s.Operation, Date = s.Date,
                Description = s.Description, CategoryId = s.Category is null ? null : Category(s.Scope, s.Category),
                Amount = Cents(s.Amount), PaymentMethodId = method.Id, Installments = Math.Max(1, s.Installments),
                FirstDueDate = s.FirstDueDate, Status = s.Status, IsPriorCommitment = s.PriorCommitment,
                PaidCardId = s.PaidCard is null ? null : Method(s.PaidCard).Id,
                RecurringRuleId = s.Rule is null ? null : rules[s.Rule].Id,
            };
            if (method.IsCard && t.Operation != Operation.PagoTarjeta)
                t.FirstDueDate ??= CardCalendar.ProposeDueDate(method.Cycles, t.Date);
            if (method.IsCard && t.Operation != Operation.PagoTarjeta && t.FirstDueDate is null)
            {
                warnings.Add($"Sin vencimiento para {t.Description} del {t.Date}: falta el cierre de {method.Name}.");
                continue;
            }
            t.Impacts = CashImpactBuilder.Build(t, method, sc => boxes[sc]);
            db.Transactions.Add(t);
        }
        await db.SaveChangesAsync();

        foreach (var rule in rules.Values)
            foreach (var m in await RecurringRuleSync.RegenerateAsync(db, rule, today))
                warnings.Add($"Gasto fijo {rule.Description}: falta el cierre de la tarjeta para {m:yyyy-MM}.");
        await db.SaveChangesAsync();

        return new ImportResult(snap.Transactions.Count - warnings.Count(w => w.StartsWith("Sin vencimiento")), rules.Count,
            snap.Budgets.Count, categories.Count, snap.CardCycles.Count, warnings);
    }
}
