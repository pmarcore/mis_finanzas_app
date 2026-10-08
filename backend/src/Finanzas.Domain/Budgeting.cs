namespace Finanzas.Domain;

/// <summary>Gasto estimado de un mes futuro que todavía no está cargado como movimiento ni como gasto fijo.</summary>
public record BudgetEstimate(Scope Scope, DateOnly Date, long Amount);

public record BudgetRow(Category Category, long Budget, long Actual, long Available, decimal UsePct, long Projected, string Alert);

public static class Budgeting
{
    /// <summary>Lo que cuenta como consumo del presupuesto: gastos y cuota del auto realizados, con tarjeta el día de la compra.</summary>
    public static bool ConsumesBudget(Transaction t) =>
        t.Operation is Operation.Gasto or Operation.CuotaAuto && t.Status == TransactionStatus.Realizado && !t.IsPriorCommitment;

    /// <summary>Presupuesto vigente de un mes: el del mes o, si no se cargó, el último mes anterior cargado.</summary>
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
        var monthBudgets = BudgetsFor(budgets, first).ToDictionary(b => b.CategoryId, b => b.Amount);

        var actuals = transactions
            .Where(t => t.Scope == scope && ConsumesBudget(t) && t.CategoryId is not null && t.Date >= first && t.Date <= upTo)
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
        var result = new List<BudgetEstimate>();
        for (var m = new DateOnly(cutoff.Year, cutoff.Month, 1).AddMonths(1); m <= until; m = m.AddMonths(1))
        {
            var covered = rules
                .Where(r => r.Scope == scope && r.Operation is Operation.Gasto or Operation.CuotaAuto && r.StartMonth <= m && (r.EndMonth is null || r.EndMonth >= m))
                .GroupBy(r => r.CategoryId)
                .ToDictionary(g => g.Key, g => g.Sum(r => RecurringRuleExpander.AmountFor(r, m)));
            var amount = BudgetsFor(budgets, m)
                .Where(b => expenseCats.Contains(b.CategoryId))
                .Sum(b => Math.Max(0, b.Amount - covered.GetValueOrDefault(b.CategoryId)));
            var date = new DateOnly(m.Year, m.Month, 20);
            if (amount > 0 && date <= until) result.Add(new BudgetEstimate(scope, date, amount));
        }
        return result;
    }
}
