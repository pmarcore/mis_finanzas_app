using Finanzas.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Infrastructure;

/// <summary>Una compra que cambió de resumen al recalcular (mes del primer resumen antes y después).</summary>
public record StatementMove(int TransactionId, string Description, DateOnly Date, DateOnly FromMonth, DateOnly ToMonth);

/// <summary>
/// Mantiene las compras con tarjeta consistentes con los cierres cargados: el resumen de cada compra sale de su fecha de
/// compra (salvo importadas, compromisos previos o fijadas a mano) y la fecha de cada cuota es el vencimiento de su mes.
/// </summary>
public static class CardStatementSync
{
    /// <summary>
    /// Recalcula las compras de una tarjeta (o de todas) y los vencimientos de los pagos de esa tarjeta. No guarda: el llamador
    /// hace SaveChanges. Devuelve las compras que cambiaron de resumen.
    /// </summary>
    public static async Task<List<StatementMove>> RecalculateAsync(FinanzasDbContext db, int? cardId = null)
    {
        var methods = await db.PaymentMethods.Include(m => m.Cycles).Where(m => m.Type == PaymentMethodType.Tarjeta)
            .Where(m => cardId == null || m.Id == cardId).ToDictionaryAsync(m => m.Id);
        var boxes = await db.CashBoxes.ToDictionaryAsync(b => b.Scope, b => b.Id);
        var ids = methods.Keys.ToList();
        var moves = new List<StatementMove>();

        var purchases = await db.Transactions.Include(t => t.Impacts).Include(t => t.OperationType)
            .Where(t => ids.Contains(t.PaymentMethodId)).ToListAsync();
        foreach (var t in purchases)
        {
            var method = methods[t.PaymentMethodId];
            var before = t.FirstStatementMonth ?? (t.FirstDueDate is { } d ? (DateOnly?)CardCalendar.MonthOf(d) : null);
            var month = before;
            if (!t.KeepsStatementMonth && CardCalendar.ProposeStatementMonth(method.Cycles, t.Date) is { } proposed) month = proposed;
            if (month is null) continue;
            t.FirstStatementMonth = month;

            var rebuilt = CashImpactBuilder.Build(t, method, s => boxes[s]);
            if (!SameImpacts(t.Impacts, rebuilt))
            {
                db.CashImpacts.RemoveRange(t.Impacts);
                t.Impacts = rebuilt;
            }
            if (before is { } b && b != month)
                moves.Add(new StatementMove(t.Id, t.Description, t.Date, b, month.Value));
        }

        // Un pago de tarjeta sigue atado al mes que paga; solo se actualiza la fecha de vencimiento que muestra.
        var payments = await db.Transactions
            .Where(t => t.OperationType.Kind == OperationKind.PagoTarjeta && t.PaidCardId != null && ids.Contains(t.PaidCardId.Value))
            .ToListAsync();
        foreach (var p in payments)
        {
            if (CashFlowCalculator.PaidMonthOf(p) is not { } m) continue;
            p.FirstStatementMonth = m;
            p.FirstDueDate = CardCalendar.DueDateFor(methods[p.PaidCardId!.Value].Cycles, m, p.FirstDueDate?.Day);
        }
        return moves;
    }

    static bool SameImpacts(List<CashImpact> current, List<CashImpact> rebuilt) =>
        current.Count == rebuilt.Count && current.OrderBy(i => i.InstallmentNumber).Zip(rebuilt.OrderBy(i => i.InstallmentNumber))
            .All(p => p.First.CashBoxId == p.Second.CashBoxId && p.First.CardId == p.Second.CardId && p.First.ImpactDate == p.Second.ImpactDate
                      && p.First.StatementMonth == p.Second.StatementMonth && p.First.Amount == p.Second.Amount
                      && p.First.InstallmentNumber == p.Second.InstallmentNumber);
}
