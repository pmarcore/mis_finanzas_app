namespace Finanzas.Domain;

/// <summary>Gasto estimado de un mes futuro que todavía no está cargado como movimiento ni como gasto fijo.</summary>
public record BudgetEstimate(Scope Scope, DateOnly Date, long Amount);

public record BudgetRow(Category Category, long Budget, long Actual, long Available, decimal UsePct, long Projected, string Alert);

/// <summary>Fuente base para proyectar el presupuesto de meses futuros.</summary>
public enum BudgetSource { Presupuesto = 1, Real = 2 }

/// <summary>De dónde sale el monto propuesto: la base ajustada, los fijos del mes o el mayor de los dos.</summary>
public enum ProposalOrigin { Base = 1, Fijos = 2, Mayor = 3 }

public record ProposalRow(Category Category, long Base, long? Fixed, long Proposed, ProposalOrigin Origin);

public record ProposalMonth(DateOnly Month, bool HasOwnBudget, List<ProposalRow> Rows);

public static class Budgeting
{
    /// <summary>Lo que cuenta como consumo del presupuesto: movimientos que restan de caja (gastos, cuota del auto) realizados,
    /// con tarjeta el día de la compra. Ahorro, inversión, transferencias y pagos de tarjeta no consumen.</summary>
    public static bool ConsumesBudget(Transaction t) =>
        t.OperationType.IsExpense && t.Status == TransactionStatus.Realizado && !t.IsPriorCommitment;

    /// <summary>
    /// Presupuesto de un ámbito: cada categoría es de Familia o de Memey, así que el presupuesto queda separado por la categoría.
    /// Se filtra antes de buscar el mes vigente para que cargar uno no tape al otro.
    /// </summary>
    public static List<Budget> OfScope(IEnumerable<Budget> budgets, IEnumerable<Category> categories, Scope scope)
    {
        var ids = categories.Where(c => c.Scope == scope).Select(c => c.Id).ToHashSet();
        return budgets.Where(b => ids.Contains(b.CategoryId)).ToList();
    }

    /// <summary>Movimientos que forman el real de una categoría en un mes al corte (lo que suma la columna Real del reporte).</summary>
    public static IEnumerable<Transaction> ActualTransactions(IEnumerable<Transaction> transactions, Scope scope, DateOnly month, DateOnly cutoff)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddDays(DateTime.DaysInMonth(month.Year, month.Month) - 1);
        var upTo = cutoff < last ? cutoff : last;
        return transactions.Where(t => t.Scope == scope && ConsumesBudget(t) && t.CategoryId is not null && t.Date >= first && t.Date <= upTo);
    }

    /// <summary>Presupuesto vigente de un mes: el del mes o, si no se cargó, el último mes anterior cargado.
    /// Recibe el presupuesto de un solo ámbito (ver <see cref="OfScope"/>).</summary>
    public static IReadOnlyList<Budget> BudgetsFor(IEnumerable<Budget> budgets, DateOnly month)
    {
        var latest = budgets.Where(b => b.Month <= month).Select(b => b.Month).DefaultIfEmpty().Max();
        return latest == default ? [] : budgets.Where(b => b.Month == latest).ToList();
    }

    /// <summary>
    /// Presupuesto vs real de un mes al corte. Variables: proyecta el ritmo diario al mes completo.
    /// Fijos y deuda: el mayor entre presupuesto y real. Alertas según los umbrales de Configuración.
    /// </summary>
    public static List<BudgetRow> Report(
        IEnumerable<Category> categories, IEnumerable<Budget> budgets, IEnumerable<Transaction> transactions,
        Scope scope, DateOnly month, DateOnly cutoff, Settings settings)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var last = first.AddDays(days - 1);
        var upTo = cutoff < last ? cutoff : last;
        var monthBudgets = BudgetsFor(OfScope(budgets, categories, scope), first).ToDictionary(b => b.CategoryId, b => b.Amount);

        var actuals = ActualTransactions(transactions, scope, first, cutoff)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        return categories
            .Where(c => c.Scope == scope && (monthBudgets.ContainsKey(c.Id) || actuals.ContainsKey(c.Id)))
            .Select(c =>
            {
                var budget = monthBudgets.GetValueOrDefault(c.Id);
                var actual = actuals.GetValueOrDefault(c.Id);
                var projected = cutoff < first ? budget
                    : cutoff >= last ? actual
                    : c.BudgetType == BudgetType.Variable ? (long)Math.Round((decimal)actual * days / upTo.Day, MidpointRounding.AwayFromZero)
                    : Math.Max(budget, actual);
                var pct = budget == 0 ? (actual == 0 ? 0 : 999.9m) : Math.Round(100m * actual / budget, 1);
                return new BudgetRow(c, budget, actual, budget - actual, pct, projected, AlertFor(pct, projected > budget, settings));
            })
            .OrderBy(r => r.Category.Name, StringComparer.Create(new System.Globalization.CultureInfo("es-AR"), true))
            .ToList();
    }

    public static string AlertFor(decimal pct, bool projectsExcess, Settings s) =>
        pct >= s.AlertCriticalPct ? "Critico"
        : pct >= s.AlertExceededPct ? "Excedido"
        : pct >= s.AlertControlPct ? "Control"
        : pct >= s.AlertAttentionPct ? "Preventiva"
        : projectsExcess ? "Proyecta exceso"
        : "Normal";

    /// <summary>
    /// Para los meses posteriores al corte: presupuesto de gastos menos lo que ya cubren los gastos fijos cargados.
    /// Se descuenta el día 20 de cada mes. A medida que se cargan más fijos, el estimado baja.
    /// </summary>
    public static List<BudgetEstimate> MonthlyEstimates(
        IEnumerable<Category> categories, IEnumerable<Budget> budgets, IEnumerable<RecurringRule> rules,
        Scope scope, DateOnly cutoff, DateOnly until)
    {
        var expenseCats = categories.Where(c => c.Scope == scope && c.Kind == CategoryKind.Gasto).Select(c => c.Id).ToHashSet();
        var scopeBudgets = OfScope(budgets, categories, scope);
        var result = new List<BudgetEstimate>();
        for (var m = new DateOnly(cutoff.Year, cutoff.Month, 1).AddMonths(1); m <= until; m = m.AddMonths(1))
        {
            var covered = rules
                .Where(r => r.Scope == scope && r.OperationType.IsExpense && r.StartMonth <= m && (r.EndMonth is null || r.EndMonth >= m))
                .GroupBy(r => r.CategoryId)
                .ToDictionary(g => g.Key, g => g.Sum(r => RecurringRuleExpander.AmountFor(r, m)));
            var amount = BudgetsFor(scopeBudgets, m)
                .Where(b => expenseCats.Contains(b.CategoryId))
                .Sum(b => Math.Max(0, b.Amount - covered.GetValueOrDefault(b.CategoryId)));
            var date = new DateOnly(m.Year, m.Month, 20);
            if (amount > 0 && date <= until) result.Add(new BudgetEstimate(scope, date, amount));
        }
        return result;
    }

    /// <summary>Mes con presupuesto propio del ámbito; si no tiene, el último mes anterior del que hereda (o null).
    /// Recibe el presupuesto de un solo ámbito (ver <see cref="OfScope"/>).</summary>
    public static (bool HasOwn, DateOnly? InheritedFrom) SourceOf(IReadOnlyCollection<Budget> budgets, DateOnly month)
    {
        if (budgets.Any(b => b.Month == month)) return (true, null);
        var from = budgets.Where(b => b.Month < month).Select(b => b.Month).DefaultIfEmpty().Max();
        return (false, from == default ? null : from);
    }

    /// <summary>Redondea a los $1.000 más cercanos (montos en centavos).</summary>
    public static long RoundToThousand(long cents) => (long)Math.Round(cents / 100_000m, MidpointRounding.AwayFromZero) * 100_000;

    /// <summary>
    /// Borrador del presupuesto de los meses from..to de un ámbito. La base es el presupuesto vigente del mes fuente o su real
    /// (mes completo), ajustada por el % y redondeada a $1.000. Con fijos, las categorías Fijo y Deuda toman el monto de los
    /// gastos fijos vigentes en cada mes y las Variable el mayor entre base y fijos. Solo categorías de gasto activas.
    /// </summary>
    public static List<ProposalMonth> Propose(
        IEnumerable<Category> categories, IEnumerable<Budget> budgets, IEnumerable<Transaction> transactions, IEnumerable<RecurringRule> rules,
        Scope scope, BudgetSource source, DateOnly sourceMonth, DateOnly from, DateOnly to, bool useFixed, decimal? increasePct)
    {
        var cats = categories.Where(c => c.Scope == scope && c.Kind == CategoryKind.Gasto && c.Active).ToList();
        var scopeBudgets = OfScope(budgets, categories, scope);
        var sourceFirst = new DateOnly(sourceMonth.Year, sourceMonth.Month, 1);
        var bases = source == BudgetSource.Presupuesto
            ? BudgetsFor(scopeBudgets, sourceFirst).GroupBy(b => b.CategoryId).ToDictionary(g => g.Key, g => g.Sum(b => b.Amount))
            : ActualTransactions(transactions, scope, sourceFirst, sourceFirst.AddMonths(1).AddDays(-1))
                .GroupBy(t => t.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var factor = 1 + (increasePct ?? 0) / 100m;
        var adjusted = bases.ToDictionary(kv => kv.Key, kv => RoundToThousand((long)Math.Round(kv.Value * factor, MidpointRounding.AwayFromZero)));
        var expenseRules = rules.Where(r => r.Scope == scope && r.OperationType.IsExpense).ToList();
        var collator = StringComparer.Create(new System.Globalization.CultureInfo("es-AR"), true);

        var result = new List<ProposalMonth>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var fixedByCat = useFixed
                ? expenseRules.Where(r => r.StartMonth <= m && (r.EndMonth is null || r.EndMonth >= m))
                    .GroupBy(r => r.CategoryId).ToDictionary(g => g.Key, g => g.Sum(r => RecurringRuleExpander.AmountFor(r, m)))
                : [];
            var rows = cats
                .Select(c =>
                {
                    var b = adjusted.GetValueOrDefault(c.Id);
                    if (!fixedByCat.TryGetValue(c.Id, out var f)) return new ProposalRow(c, b, null, b, ProposalOrigin.Base);
                    if (c.BudgetType != BudgetType.Variable) return new ProposalRow(c, b, f, f, ProposalOrigin.Fijos);
                    return new ProposalRow(c, b, f, Math.Max(b, f), ProposalOrigin.Mayor);
                })
                .Where(r => r.Proposed > 0)
                .OrderBy(r => r.Category.Name, collator)
                .ToList();
            result.Add(new ProposalMonth(m, scopeBudgets.Any(b => b.Month == m), rows));
        }
        return result;
    }
}
