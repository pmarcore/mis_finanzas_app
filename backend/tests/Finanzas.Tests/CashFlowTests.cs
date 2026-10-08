using Finanzas.Domain;

namespace Finanzas.Tests;

public class CashFlowTests
{
    static readonly DateOnly Oct1 = new(2026, 10, 1);
    static readonly DateOnly Cutoff = new(2026, 10, 8);

    static PaymentMethod Debit => new() { Id = 1, Name = "Débito", Type = PaymentMethodType.Debito, CashBoxId = 1 };
    static PaymentMethod Visa => new()
    {
        Id = 4, Name = "Visa Santander", Type = PaymentMethodType.Tarjeta, CashBoxId = 1,
        Cycles =
        [
            new() { ClosingDate = new(2026, 9, 28), DueDate = new(2026, 10, 9) },
            new() { ClosingDate = new(2026, 10, 29), DueDate = new(2026, 11, 11) },
            new() { ClosingDate = new(2026, 11, 26), DueDate = new(2026, 12, 11) },
        ],
    };
    static readonly List<CashBox> Boxes =
    [
        new() { Id = 1, Scope = Scope.Familia, Name = "Familia", OpeningBalance = 1_000_000_00, OpeningDate = Oct1 },
        new() { Id = 2, Scope = Scope.Memey, Name = "Memey MP", OpeningBalance = 0, OpeningDate = Oct1 },
    ];
    static int BoxOf(Scope s) => s == Scope.Familia ? 1 : 2;

    static Transaction Tx(Operation op, DateOnly date, long amount, PaymentMethod method, TransactionStatus status = TransactionStatus.Realizado, int n = 1, DateOnly? due = null, int? paidCard = null)
    {
        var t = new Transaction { Operation = op, Date = date, Amount = amount, PaymentMethodId = method.Id, Installments = n, Status = status, Description = "x", PaidCardId = paidCard };
        t.FirstDueDate = due ?? (method.IsCard && op != Operation.PagoTarjeta ? CardCalendar.ProposeDueDate(method.Cycles, date) : null);
        t.Impacts = CashImpactBuilder.Build(t, method, BoxOf);
        return t;
    }

    [Fact]
    public void Purchase_before_closing_is_due_the_next_month()
    {
        Assert.Equal(new DateOnly(2026, 11, 11), CardCalendar.ProposeDueDate(Visa.Cycles, new(2026, 10, 20)));
        Assert.Equal(new DateOnly(2026, 12, 11), CardCalendar.ProposeDueDate(Visa.Cycles, new(2026, 10, 30)));
        Assert.Null(CardCalendar.ProposeDueDate(Visa.Cycles, new(2027, 1, 5)));
    }

    [Fact]
    public void Installments_split_the_total_and_keep_the_remainder_in_the_first()
    {
        var t = Tx(Operation.Gasto, new(2026, 10, 8), 100_000_01, Visa, n: 3);
        Assert.Equal([new(2026, 11, 11), new(2026, 12, 11), new(2027, 1, 11)], t.Impacts.Select(i => i.ImpactDate));
        Assert.Equal(-100_000_01, t.Impacts.Sum(i => i.Amount));
        Assert.All(t.Impacts, i => Assert.Equal(4, i.CardId));
    }

    [Fact]
    public void Card_purchase_does_not_touch_cash_until_its_due_date()
    {
        var calc = new CashFlowCalculator(Boxes, [Tx(Operation.Gasto, new(2026, 10, 5), 50_000_00, Visa)], []);
        Assert.Equal(1_000_000_00, calc.CashAt(Scope.Familia, Cutoff));
        Assert.Equal(1_000_000_00, calc.ProjectedAt(Scope.Familia, Cutoff, new(2026, 10, 31)));
        Assert.Equal(950_000_00, calc.ProjectedAt(Scope.Familia, Cutoff, new(2026, 11, 30)));
    }

    [Fact]
    public void Real_statement_wins_over_modeled_and_they_are_never_added()
    {
        var purchase = Tx(Operation.Gasto, new(2026, 10, 5), 50_000_00, Visa);
        var statement = new Statement { PaymentMethodId = 4, DueDate = new(2026, 11, 11), ConfirmedAmount = 80_000_00 };
        var calc = new CashFlowCalculator(Boxes, [purchase], [statement]);
        Assert.Equal(920_000_00, calc.ProjectedAt(Scope.Familia, Cutoff, new(2026, 11, 30)));
    }

    [Fact]
    public void Paid_statement_leaves_only_the_difference_pending()
    {
        var purchase = Tx(Operation.Gasto, new(2026, 9, 20), 50_000_00, Visa, due: new(2026, 10, 9));
        var payment = Tx(Operation.PagoTarjeta, new(2026, 10, 7), 30_000_00, Debit, due: new(2026, 10, 9), paidCard: 4);
        var calc = new CashFlowCalculator(Boxes, [purchase, payment], []);
        Assert.Equal(970_000_00, calc.CashAt(Scope.Familia, Cutoff));
        Assert.Equal(950_000_00, calc.ProjectedAt(Scope.Familia, Cutoff, new(2026, 10, 31)));
    }

    [Fact]
    public void Withdrawal_from_Memey_moves_cash_without_changing_the_total()
    {
        var mp = new PaymentMethod { Id = 3, Name = "Memey MP", Type = PaymentMethodType.Billetera, CashBoxId = 2 };
        var calc = new CashFlowCalculator(Boxes, [Tx(Operation.RetiroDeMemey, new(2026, 10, 3), 11_728_00, mp)], []);
        Assert.Equal(1_011_728_00, calc.CashAt(Scope.Familia, Cutoff));
        Assert.Equal(-11_728_00, calc.CashAt(Scope.Memey, Cutoff));
        Assert.Equal(1_000_000_00, calc.CashAt(null, Cutoff));
    }

    [Fact]
    public void Recurring_rule_uses_the_amount_in_force_and_the_card_cycle()
    {
        var rule = new RecurringRule
        {
            Id = 7, Scope = Scope.Familia, PersonId = 3, Operation = Operation.Gasto, Description = "Colegio", CategoryId = 1,
            PaymentMethodId = 4, DayOfMonth = 20, StartMonth = new(2026, 10, 1),
            Amounts = [new() { FromMonth = new(2026, 10, 1), Amount = 1_654_000_00 }, new() { FromMonth = new(2026, 12, 1), Amount = 1_800_000_00 }],
        };
        var expanded = RecurringRuleExpander.Expand(rule, Visa, new(2026, 12, 1)).ToList();
        Assert.Equal(3, expanded.Count);
        Assert.Equal(new DateOnly(2026, 11, 11), expanded[0].FirstDueDate);
        Assert.Equal(1_800_000_00, expanded[2].Amount);
        Assert.All(expanded, t => Assert.Equal(TransactionStatus.Previsto, t.Status));
    }
}
