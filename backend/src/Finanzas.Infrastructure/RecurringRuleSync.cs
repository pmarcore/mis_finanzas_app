using Finanzas.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Infrastructure;

/// <summary>Mantiene los previstos de cada gasto fijo: se regeneran al crear o cambiar la regla, sin tocar los ya realizados.</summary>
public static class RecurringRuleSync
{
    /// <summary>Hasta fin de año, y como mínimo tres meses después del mes actual para que el trimestral tenga datos.</summary>
    public static DateOnly Horizon(DateOnly today)
    {
        var endOfYear = new DateOnly(today.Year, 12, 1);
        var threeAhead = new DateOnly(today.Year, today.Month, 1).AddMonths(3);
        return endOfYear > threeAhead ? endOfYear : threeAhead;
    }

    /// <summary>Devuelve los meses que no se pudieron proyectar porque falta el cierre de la tarjeta.</summary>
    public static async Task<List<DateOnly>> RegenerateAsync(FinanzasDbContext db, RecurringRule rule, DateOnly today)
    {
        var method = await db.PaymentMethods.Include(m => m.Cycles).FirstAsync(m => m.Id == rule.PaymentMethodId);
        var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
        var existing = await db.Transactions.Include(t => t.Impacts).Where(t => t.RecurringRuleId == rule.Id).ToListAsync();

        // Los previstos anteriores al inicio de la regla (por ejemplo, los importados de la planilla) se conservan.
        var replaced = existing.Where(t => t.Status == TransactionStatus.Previsto && t.Date >= rule.StartMonth).ToList();
        db.Transactions.RemoveRange(replaced);
        var done = existing.Except(replaced).Select(t => (t.Date.Year, t.Date.Month)).ToHashSet();

        var missing = new List<DateOnly>();
        foreach (var t in RecurringRuleExpander.Expand(rule, method, Horizon(today)))
        {
            if (done.Contains((t.Date.Year, t.Date.Month))) continue;
            if (method.IsCard && t.FirstDueDate is null) { missing.Add(t.Date); continue; }
            t.Impacts = CashImpactBuilder.Build(t, method, s => boxes[s]);
            db.Transactions.Add(t);
        }
        return missing;
    }
}
