using Finanzas.Domain;

namespace Finanzas.Api;

// Contrato de la API: montos en pesos (decimal), fechas 'yyyy-MM-dd', meses 'yyyy-MM', enums en camelCase.
// Internamente el dominio guarda centavos (long).

public static class Money
{
    public static decimal Pesos(long cents) => cents / 100m;
    public static long Cents(decimal pesos) => (long)Math.Round(pesos * 100, MidpointRounding.AwayFromZero);
}

public static class Months
{
    public static string Format(DateOnly d) => d.ToString("yyyy-MM");
    public static DateOnly Parse(string month) => DateOnly.ParseExact(month + "-01", "yyyy-MM-dd");
    public static bool TryParse(string? month, out DateOnly first) =>
        DateOnly.TryParseExact((month ?? "") + "-01", "yyyy-MM-dd", out first);
    public static DateOnly EndOf(DateOnly d) => new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
}

public record TransactionDto(
    int Id, Scope Scope, int PersonId, Operation Operation, DateOnly Date, string Description, int? CategoryId,
    decimal Amount, int PaymentMethodId, int Installments, DateOnly? FirstDueDate, TransactionStatus Status,
    int? RecurringRuleId, int? PaidCardId, bool IsPriorCommitment)
{
    public static TransactionDto From(Transaction t) => new(
        t.Id, t.Scope, t.PersonId, t.Operation, t.Date, t.Description, t.CategoryId, Money.Pesos(t.Amount), t.PaymentMethodId,
        t.Installments, t.FirstDueDate, t.Status, t.RecurringRuleId, t.PaidCardId, t.IsPriorCommitment);
}

public record NewTransactionDto(
    Scope Scope, int PersonId, Operation Operation, DateOnly Date, string Description, int? CategoryId, decimal Amount,
    int PaymentMethodId, int Installments = 1, DateOnly? FirstDueDate = null, TransactionStatus? Status = null, int? PaidCardId = null)
{
    public Transaction ToEntity() => new()
    {
        Scope = Scope, PersonId = PersonId, Operation = Operation, Date = Date, Description = Description, CategoryId = CategoryId,
        Amount = Money.Cents(Amount), PaymentMethodId = PaymentMethodId, Installments = Math.Max(1, Installments),
        FirstDueDate = FirstDueDate, Status = Status ?? TransactionStatus.Realizado, PaidCardId = PaidCardId,
    };
}

public record ConfirmRequest(DateOnly? Date, decimal? Amount);

public record CardCycleDto(int CardId, string Month, DateOnly ClosingDate, DateOnly DueDate)
{
    public static CardCycleDto From(CardCycle c) => new(c.PaymentMethodId, Months.Format(c.ClosingDate), c.ClosingDate, c.DueDate);
}

public record CardDueDateDto(int CardId, DateOnly PurchaseDate, DateOnly FirstDueDate);

public record RecurringAmountDto(string FromMonth, decimal Amount);

public record RecurringRuleDto(
    int Id, Scope Scope, int PersonId, Operation Operation, string Description, int CategoryId, int PaymentMethodId,
    int DayOfMonth, string StartMonth, string? EndMonth, List<RecurringAmountDto> Amounts)
{
    public static RecurringRuleDto From(RecurringRule r) => new(
        r.Id, r.Scope, r.PersonId, r.Operation, r.Description, r.CategoryId, r.PaymentMethodId, r.DayOfMonth,
        Months.Format(r.StartMonth), r.EndMonth is { } e ? Months.Format(e) : null,
        r.Amounts.OrderBy(a => a.FromMonth).Select(a => new RecurringAmountDto(Months.Format(a.FromMonth), Money.Pesos(a.Amount))).ToList());

    public void ApplyTo(RecurringRule r)
    {
        r.Scope = Scope; r.PersonId = PersonId; r.Operation = Operation; r.Description = Description; r.CategoryId = CategoryId;
        r.PaymentMethodId = PaymentMethodId; r.DayOfMonth = Math.Clamp(DayOfMonth, 1, 31);
        r.StartMonth = Months.Parse(StartMonth); r.EndMonth = EndMonth is null ? null : Months.Parse(EndMonth);
        r.Amounts = Amounts.Select(a => new RecurringRuleAmount { FromMonth = Months.Parse(a.FromMonth), Amount = Money.Cents(a.Amount) }).ToList();
    }
}

public record BudgetDto(string Month, int CategoryId, decimal Amount);

public record BudgetVsActualRowDto(
    int CategoryId, string Category, BudgetType BudgetType, decimal Budget, decimal Actual, decimal Available,
    decimal UsePct, decimal Projected, string Alert);

public record BudgetVsActualDto(
    string Month, DateOnly Cutoff, Scope Scope, List<BudgetVsActualRowDto> Rows,
    decimal TotalBudget, decimal TotalActual, decimal TotalProjected);

public record CashBoxDto(int Id, Scope Scope, string Name, decimal OpeningBalance, DateOnly OpeningDate, decimal? ObservedBalance, DateOnly? ObservedDate)
{
    public static CashBoxDto From(CashBox b) => new(b.Id, b.Scope, b.Name, Money.Pesos(b.OpeningBalance), b.OpeningDate,
        b.ObservedBalance is { } o ? Money.Pesos(o) : null, b.ObservedDate);
}

public record SettingsDto(
    List<CashBoxDto> CashBoxes, decimal CashFloor, decimal SavingsGoal,
    int AlertAttentionPct, int AlertControlPct, int AlertExceededPct, int AlertCriticalPct);

public record UpdateSettingsDto(decimal CashFloor, decimal SavingsGoal, int AlertAttentionPct, int AlertControlPct, int AlertExceededPct, int AlertCriticalPct);

public record PaymentMethodDto(int Id, string Name, PaymentMethodType Type, int CashBoxId, bool IsCard)
{
    public static PaymentMethodDto From(PaymentMethod m) => new(m.Id, m.Name, m.Type, m.CashBoxId, m.IsCard);
}
