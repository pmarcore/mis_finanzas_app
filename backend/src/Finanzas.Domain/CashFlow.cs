namespace Finanzas.Domain;

/// <summary>
/// Reglas de la planilla (pestaña Guía) llevadas a código:
/// una compra con tarjeta consume presupuesto el día de la compra y sale de la caja en cada vencimiento;
/// el resumen real manda sobre el modelado y nunca se suman.
/// </summary>
public static class CardCalendar
{
    /// <summary>Vencimiento del primer cierre igual o posterior a la fecha de compra. Null si falta cargar el ciclo.</summary>
    public static DateOnly? ProposeDueDate(IEnumerable<CardCycle> cycles, DateOnly purchaseDate) =>
        cycles.Where(c => c.ClosingDate >= purchaseDate)
              .OrderBy(c => c.ClosingDate)
              .Select(c => (DateOnly?)c.DueDate)
              .FirstOrDefault();
}

public static class CashImpactBuilder
{
    public static int SignOf(Operation op) => op switch
    {
        Operation.Ingreso => 1,
        Operation.Gasto or Operation.PagoTarjeta or Operation.CuotaAuto or Operation.Ahorro => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(op), "Las transferencias generan dos impactos"),
    };

    /// <summary>
    /// Genera los impactos en caja de un movimiento. Con tarjeta devuelve líneas de resumen (CardId informado),
    /// que la proyección agrupa por tarjeta y vencimiento.
    /// </summary>
    public static List<CashImpact> Build(Transaction tx, PaymentMethod method, Func<Scope, int> cashBoxOf)
    {
        if (tx.Operation is Operation.AporteAMemey or Operation.RetiroDeMemey)
        {
            var (from, to) = tx.Operation == Operation.AporteAMemey ? (Scope.Familia, Scope.Memey) : (Scope.Memey, Scope.Familia);
            return
            [
                new CashImpact { CashBoxId = cashBoxOf(from), ImpactDate = tx.Date, Amount = -tx.Amount },
                new CashImpact { CashBoxId = cashBoxOf(to), ImpactDate = tx.Date, Amount = tx.Amount },
            ];
        }

        var sign = SignOf(tx.Operation);
        if (!method.IsCard)
            return [new CashImpact { CashBoxId = method.CashBoxId, ImpactDate = tx.Date, Amount = sign * tx.Amount }];

        if (tx.FirstDueDate is not { } firstDue)
            throw new InvalidOperationException("Con tarjeta, el primer vencimiento es obligatorio.");

        var n = Math.Max(1, tx.Installments);
        var installment = tx.Amount / n;
        var remainder = tx.Amount - installment * n;
        return Enumerable.Range(0, n).Select(i => new CashImpact
        {
            CashBoxId = method.CashBoxId,
            CardId = method.Id,
            ImpactDate = firstDue.AddMonths(i),
            Amount = sign * (installment + (i == 0 ? remainder : 0)),
            InstallmentNumber = i + 1,
        }).ToList();
    }
}

public static class RecurringRuleExpander
{
    /// <summary>Monto vigente en un mes: el último cambio con FromMonth menor o igual.</summary>
    public static long AmountFor(RecurringRule rule, DateOnly month) =>
        rule.Amounts.Where(a => a.FromMonth <= month).OrderByDescending(a => a.FromMonth).Select(a => a.Amount).FirstOrDefault();

    /// <summary>Previstos de la regla desde su inicio hasta untilMonth (por defecto, diciembre del año en curso).</summary>
    public static IEnumerable<Transaction> Expand(RecurringRule rule, PaymentMethod method, DateOnly untilMonth)
    {
        var end = rule.EndMonth is { } e && e < untilMonth ? e : untilMonth;
        for (var m = new DateOnly(rule.StartMonth.Year, rule.StartMonth.Month, 1); m <= end; m = m.AddMonths(1))
        {
            var day = Math.Min(rule.DayOfMonth, DateTime.DaysInMonth(m.Year, m.Month));
            var date = new DateOnly(m.Year, m.Month, day);
            yield return new Transaction
            {
                Scope = rule.Scope,
                PersonId = rule.PersonId,
                Operation = rule.Operation,
                Date = date,
                Description = rule.Description,
                CategoryId = rule.CategoryId,
                Amount = AmountFor(rule, m),
                PaymentMethodId = rule.PaymentMethodId,
                FirstDueDate = method.IsCard ? CardCalendar.ProposeDueDate(method.Cycles, date) : null,
                Status = TransactionStatus.Previsto,
                RecurringRuleId = rule.Id,
            };
        }
    }
}

/// <summary>Calcula caja al corte y proyecciones a partir de los impactos.</summary>
public class CashFlowCalculator(
    IReadOnlyList<CashBox> boxes,
    IReadOnlyList<Transaction> transactions,
    IReadOnlyList<Statement> statements)
{
    IEnumerable<CashBox> Boxes(Scope? scope) => boxes.Where(b => scope is null || b.Scope == scope);

    IEnumerable<(Transaction Tx, CashImpact Impact)> Impacts(Scope? scope)
    {
        var ids = Boxes(scope).ToDictionary(b => b.Id);
        return transactions.SelectMany(t => t.Impacts.Select(i => (t, i)))
            .Where(x => ids.TryGetValue(x.i.CashBoxId, out var box) && x.i.ImpactDate >= box.OpeningDate);
    }

    /// <summary>Saldo inicial + movimientos realizados hasta el corte. Las líneas de tarjeta no son caja: lo es el pago del resumen.</summary>
    public long CashAt(Scope? scope, DateOnly cutoff) =>
        Boxes(scope).Sum(b => b.OpeningBalance)
        + Impacts(scope).Where(x => x.Impact.CardId is null && x.Tx.Status == TransactionStatus.Realizado && x.Impact.ImpactDate <= cutoff)
                        .Sum(x => x.Impact.Amount);

    /// <summary>
    /// Caja al corte + previstos y movimientos posteriores + resúmenes de tarjeta pendientes hasta 'until'.
    /// Por tarjeta y vencimiento se toma el mayor entre lo modelado, el resumen real y el pago previsto, menos lo ya pagado.
    /// </summary>
    public long ProjectedAt(Scope? scope, DateOnly cutoff, DateOnly until)
    {
        var impacts = Impacts(scope).ToList();

        var direct = impacts
            .Where(x => x.Impact.CardId is null && x.Impact.ImpactDate <= until)
            .Where(x => !(x.Tx.Status == TransactionStatus.Realizado && x.Impact.ImpactDate <= cutoff))
            .Where(x => !(x.Tx.Operation == Operation.PagoTarjeta && x.Tx.Status == TransactionStatus.Previsto))
            .Sum(x => x.Impact.Amount);

        return CashAt(scope, cutoff) + direct - CardOutstanding(impacts, cutoff, until).Sum(s => s.Outstanding);
    }

    public IEnumerable<(int CardId, DateOnly DueDate, long Expected, long Outstanding)> CardStatements(Scope? scope, DateOnly cutoff, DateOnly until) =>
        CardOutstanding(Impacts(scope).ToList(), cutoff, until);

    IEnumerable<(int CardId, DateOnly DueDate, long Expected, long Outstanding)> CardOutstanding(
        List<(Transaction Tx, CashImpact Impact)> impacts, DateOnly cutoff, DateOnly until)
    {
        var modeled = impacts.Where(x => x.Impact.CardId is not null)
            .GroupBy(x => (Card: x.Impact.CardId!.Value, Due: x.Impact.ImpactDate))
            .ToDictionary(g => g.Key, g => -g.Sum(x => x.Impact.Amount));

        var payments = impacts.Where(x => x.Tx.Operation == Operation.PagoTarjeta && x.Tx.PaidCardId is not null && x.Tx.FirstDueDate is not null)
            .GroupBy(x => (Card: x.Tx.PaidCardId!.Value, Due: x.Tx.FirstDueDate!.Value))
            .ToDictionary(g => g.Key, g => (
                Planned: -g.Where(x => x.Tx.Status == TransactionStatus.Previsto).Sum(x => x.Impact.Amount),
                Paid: -g.Where(x => x.Tx.Status == TransactionStatus.Realizado && x.Impact.ImpactDate <= cutoff).Sum(x => x.Impact.Amount)));

        var keys = modeled.Keys.Union(payments.Keys).Where(k => k.Due <= until).Distinct();
        foreach (var k in keys.OrderBy(k => k.Due))
        {
            var confirmed = statements.FirstOrDefault(s => s.PaymentMethodId == k.Card && s.DueDate == k.Due)?.ConfirmedAmount ?? 0;
            var pay = payments.GetValueOrDefault(k);
            var expected = Math.Max(Math.Max(modeled.GetValueOrDefault(k), confirmed), pay.Planned + pay.Paid);
            yield return (k.Card, k.Due, expected, Math.Max(0, expected - pay.Paid));
        }
    }
}
