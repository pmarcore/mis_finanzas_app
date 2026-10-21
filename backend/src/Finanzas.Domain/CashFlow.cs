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
    /// <summary>
    /// Genera los impactos en caja de un movimiento. La caja es la del ámbito del movimiento (como en la planilla:
    /// un gasto familiar pagado desde Memey MP sale de la caja familiar). Con tarjeta devuelve líneas de resumen
    /// (CardId informado), que la proyección agrupa por tarjeta y vencimiento. Requiere tx.OperationType cargado.
    /// </summary>
    public static List<CashImpact> Build(Transaction tx, PaymentMethod method, Func<Scope, int> cashBoxOf)
    {
        var type = tx.OperationType ?? throw new InvalidOperationException("Falta el tipo de operación del movimiento.");
        if (type.Kind == OperationKind.Transferencia)
        {
            var to = type.TransferTo ?? throw new InvalidOperationException($"La transferencia {type.Name} no tiene caja de destino.");
            var from = to == Scope.Familia ? Scope.Memey : Scope.Familia;
            return
            [
                new CashImpact { CashBoxId = cashBoxOf(from), ImpactDate = tx.Date, Amount = -tx.Amount },
                new CashImpact { CashBoxId = cashBoxOf(to), ImpactDate = tx.Date, Amount = tx.Amount },
            ];
        }

        var sign = type.Sign;
        if (!method.IsCard)
            return [new CashImpact { CashBoxId = cashBoxOf(tx.Scope), ImpactDate = tx.Date, Amount = sign * tx.Amount }];

        if (tx.FirstDueDate is not { } firstDue)
            throw new InvalidOperationException("Con tarjeta, el primer vencimiento es obligatorio.");

        var n = Math.Max(1, tx.Installments);
        var installment = tx.Amount / n;
        var remainder = tx.Amount - installment * n;
        return Enumerable.Range(0, n).Select(i => new CashImpact
        {
            CashBoxId = cashBoxOf(tx.Scope),
            CardId = method.Id,
            ImpactDate = firstDue.AddMonths(i),
            Amount = sign * (installment + (i == 0 ? remainder : 0)),
            InstallmentNumber = i + 1,
        }).ToList();
    }
}

public static class RecurringRuleExpander
{
    /// <summary>
    /// Monto vigente en un mes: se recorren los cambios hasta ese mes; un monto fijo reemplaza al anterior
    /// y un incremento % se aplica sobre el vigente, así varios incrementos se acumulan.
    /// </summary>
    public static long AmountFor(RecurringRule rule, DateOnly month)
    {
        long amount = 0;
        foreach (var a in rule.Amounts.Where(a => a.FromMonth <= month).OrderBy(a => a.FromMonth))
            amount = a.IncreasePercent is { } pct ? (long)Math.Round(amount * (1 + pct / 100m), MidpointRounding.AwayFromZero) : a.Amount;
        return amount;
    }

    /// <summary>Previstos de la regla desde su inicio hasta untilMonth (por defecto, diciembre del año en curso).</summary>
    public static IEnumerable<Transaction> Expand(RecurringRule rule, PaymentMethod method, DateOnly untilMonth, DateOnly? fromMonth = null)
    {
        var end = rule.EndMonth is { } e && e < untilMonth ? e : untilMonth;
        var start = fromMonth is { } f && f > rule.StartMonth ? f : rule.StartMonth;
        for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
        {
            var day = Math.Min(rule.DayOfMonth, DateTime.DaysInMonth(m.Year, m.Month));
            var date = new DateOnly(m.Year, m.Month, day);
            yield return new Transaction
            {
                Scope = rule.Scope,
                PersonId = rule.PersonId,
                OperationTypeId = rule.OperationTypeId,
                OperationType = rule.OperationType,
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
    IReadOnlyList<Statement> statements,
    IReadOnlyList<BudgetEstimate>? estimates = null)
{
    static bool IsCardPayment(Transaction t) => t.OperationType.Kind == OperationKind.PagoTarjeta;

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
            .Where(x => !(IsCardPayment(x.Tx) && x.Tx.Status == TransactionStatus.Previsto))
            .Sum(x => x.Impact.Amount);

        var estimated = (estimates ?? []).Where(e => (scope is null || e.Scope == scope) && e.Date > cutoff && e.Date <= until).Sum(e => e.Amount);

        return CashAt(scope, cutoff) + direct - CardOutstanding(impacts, cutoff, until).Sum(s => s.Outstanding) - estimated;
    }

    /// <summary>
    /// Movimientos de caja ordenados, con saldo acumulado: desde el inicio del mes del corte hasta el corte (realizados)
    /// y después del corte hasta 'until' (pendientes, previstos, resúmenes de tarjeta y gasto estimado).
    /// El saldo de la última línea realizada es <see cref="CashAt"/> y el de la última pendiente es <see cref="ProjectedAt"/>.
    /// </summary>
    public CashLedger Ledger(Scope? scope, DateOnly cutoff, DateOnly until)
    {
        var from = new DateOnly(cutoff.Year, cutoff.Month, 1);
        var opening = CashAt(scope, from.AddDays(-1));
        var impacts = Impacts(scope).Where(x => x.Impact.CardId is null).ToList();

        // En la vista total una transferencia Familia↔Memey suma cero: se agrupa por movimiento y se omite.
        static IEnumerable<CashLedgerLine> ByTransaction(IEnumerable<(Transaction Tx, CashImpact Impact)> xs) =>
            xs.GroupBy(x => (x.Tx, x.Impact.ImpactDate))
              .Select(g => new CashLedgerLine(g.Key.ImpactDate, g.Key.Tx, null, g.Sum(x => x.Impact.Amount)))
              .Where(l => l.Amount != 0);

        var done = ByTransaction(impacts.Where(x => x.Tx.Status == TransactionStatus.Realizado && x.Impact.ImpactDate >= from && x.Impact.ImpactDate <= cutoff))
            .OrderBy(l => l.Date).ThenBy(l => l.Transaction!.Id).ToList();

        var pending = ByTransaction(impacts
                .Where(x => x.Impact.ImpactDate <= until)
                .Where(x => !(x.Tx.Status == TransactionStatus.Realizado && x.Impact.ImpactDate <= cutoff))
                .Where(x => !(IsCardPayment(x.Tx) && x.Tx.Status == TransactionStatus.Previsto)))
            .Concat(CardOutstanding(Impacts(scope).ToList(), cutoff, until).Where(s => s.Outstanding > 0)
                .Select(s => new CashLedgerLine(s.DueDate, null, s.CardId, -s.Outstanding)))
            .Concat((estimates ?? []).Where(e => (scope is null || e.Scope == scope) && e.Date > cutoff && e.Date <= until)
                .Select(e => new CashLedgerLine(e.Date, null, null, -e.Amount)))
            .OrderBy(l => l.Date).ThenBy(l => l.Transaction?.Id ?? int.MaxValue).ToList();

        return new CashLedger(from, opening, done, pending);
    }

    public IEnumerable<(int CardId, DateOnly DueDate, long Expected, long Outstanding)> CardStatements(Scope? scope, DateOnly cutoff, DateOnly until) =>
        CardOutstanding(Impacts(scope).ToList(), cutoff, until);

    IEnumerable<(int CardId, DateOnly DueDate, long Expected, long Outstanding)> CardOutstanding(
        List<(Transaction Tx, CashImpact Impact)> impacts, DateOnly cutoff, DateOnly until)
    {
        var modeled = impacts.Where(x => x.Impact.CardId is not null)
            .GroupBy(x => (Card: x.Impact.CardId!.Value, Due: x.Impact.ImpactDate))
            .ToDictionary(g => g.Key, g => -g.Sum(x => x.Impact.Amount));

        var payments = impacts.Where(x => IsCardPayment(x.Tx) && x.Tx.PaidCardId is not null && x.Tx.FirstDueDate is not null)
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

/// <summary>Saldos de ahorro e inversión: lo que salió de la caja hacia ellos menos lo que volvió. Se ven aparte de la caja.</summary>
public static class Savings
{
    /// <summary>Saldo aportado a un destino (Ahorro o Inversión) de un ámbito con los movimientos realizados hasta el corte.</summary>
    public static long BalanceAt(IEnumerable<Transaction> transactions, Scope scope, OperationKind kind, DateOnly cutoff) =>
        Movements(transactions, scope, kind).Where(t => t.Status == TransactionStatus.Realizado && t.Date <= cutoff)
            .Sum(t => -t.OperationType.Sign * t.Amount);

    public static IEnumerable<Transaction> Movements(IEnumerable<Transaction> transactions, Scope scope, OperationKind kind) =>
        transactions.Where(t => t.Scope == scope && t.OperationType.Kind == kind);
}

/// <summary>Línea de caja: un movimiento (Transaction), un resumen de tarjeta pendiente (CardId) o el gasto estimado del presupuesto (ambos null).</summary>
public record CashLedgerLine(DateOnly Date, Transaction? Transaction, int? CardId, long Amount);

public record CashLedger(DateOnly From, long Opening, IReadOnlyList<CashLedgerLine> Done, IReadOnlyList<CashLedgerLine> Pending);
