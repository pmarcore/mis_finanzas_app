namespace Finanzas.Domain;

// Montos en centavos (long) para evitar errores de redondeo. Fechas sin hora (DateOnly).

public enum Scope { Familia = 1, Memey = 2 }

public enum Operation
{
    Ingreso = 1,
    Gasto = 2,
    PagoTarjeta = 3,
    CuotaAuto = 4,
    Ahorro = 5,
    AporteAMemey = 6,
    RetiroDeMemey = 7,
}

public enum TransactionStatus { Realizado = 1, Previsto = 2 }

public enum PaymentMethodType { Debito = 1, Efectivo = 2, Billetera = 3, Tarjeta = 4 }

public enum CategoryKind { Ingreso = 1, Gasto = 2 }

public enum BudgetType { Fijo = 1, Variable = 2, Deuda = 3 }

public class Person
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

/// <summary>Una caja por ámbito: caja familiar consolidada y Memey Mercado Pago.</summary>
public class CashBox
{
    public int Id { get; set; }
    public Scope Scope { get; set; }
    public required string Name { get; set; }
    public long OpeningBalance { get; set; }
    public DateOnly OpeningDate { get; set; }
    public long? ObservedBalance { get; set; }
    public DateOnly? ObservedDate { get; set; }
}

public class PaymentMethod
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public PaymentMethodType Type { get; set; }
    /// <summary>Caja de la que sale la plata (para tarjeta: la caja que paga el resumen).</summary>
    public int CashBoxId { get; set; }
    public long OpeningCardDebt { get; set; }
    public List<CardCycle> Cycles { get; set; } = [];

    public bool IsCard => Type == PaymentMethodType.Tarjeta;
}

/// <summary>Cierre y vencimiento de una tarjeta para un mes. Se carga en Configuración.</summary>
public class CardCycle
{
    public int Id { get; set; }
    public int PaymentMethodId { get; set; }
    public DateOnly ClosingDate { get; set; }
    public DateOnly DueDate { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public Scope Scope { get; set; }
    public CategoryKind Kind { get; set; }
    public BudgetType BudgetType { get; set; }
}

public class Transaction
{
    public int Id { get; set; }
    public Scope Scope { get; set; }
    public int PersonId { get; set; }
    public Operation Operation { get; set; }
    /// <summary>Fecha de compra o del hecho; define el consumo en el presupuesto.</summary>
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public int? CategoryId { get; set; }
    /// <summary>Monto total, incluidos intereses si es en cuotas.</summary>
    public long Amount { get; set; }
    public int PaymentMethodId { get; set; }
    public int Installments { get; set; } = 1;
    /// <summary>Con tarjeta: primer vencimiento, obligatorio, propuesto según CardCycle y editable.
    /// En un pago de tarjeta: el vencimiento del resumen que se paga.</summary>
    public DateOnly? FirstDueDate { get; set; }
    public TransactionStatus Status { get; set; }
    public int? RecurringRuleId { get; set; }
    /// <summary>Tarjeta cuyo resumen se paga, solo para Operation.PagoTarjeta.</summary>
    public int? PaidCardId { get; set; }
    /// <summary>Compra anterior a la apertura: solo proyecta cuotas, no consume presupuesto.</summary>
    public bool IsPriorCommitment { get; set; }
    public string? ImportId { get; set; }
    public List<CashImpact> Impacts { get; set; } = [];
}

/// <summary>Una entrada o salida de caja. Las proyecciones leen solo esta tabla.</summary>
public class CashImpact
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int CashBoxId { get; set; }
    /// <summary>Informado en compras con tarjeta: es una línea del resumen, no caja directa.</summary>
    public int? CardId { get; set; }
    public DateOnly ImpactDate { get; set; }
    /// <summary>Positivo entra a la caja, negativo sale.</summary>
    public long Amount { get; set; }
    public int InstallmentNumber { get; set; } = 1;
}

/// <summary>Resumen de una tarjeta en un vencimiento: si el real supera al modelado, manda el real.</summary>
public class Statement
{
    public int Id { get; set; }
    public int PaymentMethodId { get; set; }
    public DateOnly DueDate { get; set; }
    public long? ConfirmedAmount { get; set; }
}

public class RecurringRule
{
    public int Id { get; set; }
    public Scope Scope { get; set; }
    public int PersonId { get; set; }
    public Operation Operation { get; set; }
    public required string Description { get; set; }
    public int CategoryId { get; set; }
    public int PaymentMethodId { get; set; }
    public int DayOfMonth { get; set; }
    public DateOnly StartMonth { get; set; }
    public DateOnly? EndMonth { get; set; }
    public List<RecurringRuleAmount> Amounts { get; set; } = [];
}

/// <summary>Monto vigente desde un mes; cambiarlo no toca los meses anteriores.</summary>
public class RecurringRuleAmount
{
    public int Id { get; set; }
    public int RecurringRuleId { get; set; }
    public DateOnly FromMonth { get; set; }
    public long Amount { get; set; }
}

public class Budget
{
    public int Id { get; set; }
    public DateOnly Month { get; set; }
    public int CategoryId { get; set; }
    public long Amount { get; set; }
}

public class Settings
{
    public int Id { get; set; }
    public long CashFloor { get; set; } = 300_000_00;
    public long SavingsGoal { get; set; } = 500_000_00;
    public int AlertAttentionPct { get; set; } = 70;
    public int AlertControlPct { get; set; } = 80;
    public int AlertExceededPct { get; set; } = 100;
    public int AlertCriticalPct { get; set; } = 120;
}
