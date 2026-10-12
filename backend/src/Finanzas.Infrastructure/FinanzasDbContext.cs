using Finanzas.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Infrastructure;

public class FinanzasDbContext(DbContextOptions<FinanzasDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<CashBox> CashBoxes => Set<CashBox>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<CardCycle> CardCycles => Set<CardCycle>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CashImpact> CashImpacts => Set<CashImpact>();
    public DbSet<Statement> Statements => Set<Statement>();
    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();
    public DbSet<RecurringRuleAmount> RecurringRuleAmounts => Set<RecurringRuleAmount>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<OperationType> OperationTypes => Set<OperationType>();
    public DbSet<InvestmentValuation> InvestmentValuations => Set<InvestmentValuation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CashBox>().HasIndex(x => x.Scope).IsUnique();

        b.Entity<PaymentMethod>().HasMany(x => x.Cycles).WithOne().HasForeignKey(c => c.PaymentMethodId);
        b.Entity<PaymentMethod>().Ignore(x => x.IsCard);
        b.Entity<CardCycle>().HasIndex(x => new { x.PaymentMethodId, x.DueDate }).IsUnique();

        // El tipo define signo y comportamiento: se carga siempre con el movimiento y con el gasto fijo.
        b.Entity<OperationType>().Ignore(x => x.Sign).Ignore(x => x.IsExpense);
        b.Entity<OperationType>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Transaction>().HasOne(x => x.OperationType).WithMany().HasForeignKey(x => x.OperationTypeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transaction>().Navigation(x => x.OperationType).AutoInclude();
        b.Entity<RecurringRule>().HasOne(x => x.OperationType).WithMany().HasForeignKey(x => x.OperationTypeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringRule>().Navigation(x => x.OperationType).AutoInclude();
        b.Entity<InvestmentValuation>().HasIndex(x => new { x.Scope, x.Date });

        b.Entity<Transaction>().HasMany(x => x.Impacts).WithOne().HasForeignKey(i => i.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Transaction>().HasIndex(x => x.Date);
        b.Entity<Transaction>().HasIndex(x => x.ImportId).IsUnique();
        b.Entity<CashImpact>().HasIndex(x => x.ImpactDate);

        b.Entity<Statement>().HasIndex(x => new { x.PaymentMethodId, x.DueDate }).IsUnique();

        b.Entity<RecurringRule>().HasMany(x => x.Amounts).WithOne().HasForeignKey(a => a.RecurringRuleId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Budget>().HasIndex(x => new { x.Month, x.CategoryId }).IsUnique();
        b.Entity<Category>().HasIndex(x => new { x.Scope, x.Name }).IsUnique();

        // Datos fijos de arranque, tomados de la planilla "Finanzas familiares y Memey".
        b.Entity<Person>().HasData(
            new Person { Id = 1, Name = "Pablo" },
            new Person { Id = 2, Name = "Rocío" },
            new Person { Id = 3, Name = "Familia" });

        b.Entity<CashBox>().HasData(
            new CashBox { Id = 1, Scope = Scope.Familia, Name = "Caja familiar consolidada", OpeningBalance = -143_464_00, OpeningDate = new DateOnly(2026, 10, 1) },
            new CashBox { Id = 2, Scope = Scope.Memey, Name = "Memey Mercado Pago", OpeningBalance = 0, OpeningDate = new DateOnly(2026, 10, 1) });

        b.Entity<PaymentMethod>().HasData(
            new PaymentMethod { Id = 1, Name = "Débito / transferencia", Type = PaymentMethodType.Debito, CashBoxId = 1 },
            new PaymentMethod { Id = 2, Name = "Efectivo", Type = PaymentMethodType.Efectivo, CashBoxId = 1 },
            new PaymentMethod { Id = 3, Name = "Memey Mercado Pago", Type = PaymentMethodType.Billetera, CashBoxId = 2 },
            new PaymentMethod { Id = 4, Name = "Visa Santander", Type = PaymentMethodType.Tarjeta, CashBoxId = 1 },
            new PaymentMethod { Id = 5, Name = "Visa Galicia", Type = PaymentMethodType.Tarjeta, CashBoxId = 1 },
            new PaymentMethod { Id = 6, Name = "Master Galicia", Type = PaymentMethodType.Tarjeta, CashBoxId = 1 },
            new PaymentMethod { Id = 7, Name = "Tarjeta Mercado Pago", Type = PaymentMethodType.Tarjeta, CashBoxId = 1 });

        b.Entity<Settings>().HasData(new Settings { Id = 1 });

        // Los 7 tipos de la planilla (mismos ids que la operación que tenían antes) más inversión y rescates.
        b.Entity<OperationType>().HasData(
            new OperationType { Id = 1, Name = "Ingreso", Kind = OperationKind.Movimiento, Effect = CashEffect.Suma, ForFamilia = true, ForMemey = true },
            new OperationType { Id = 2, Name = "Gasto", Kind = OperationKind.Movimiento, Effect = CashEffect.Resta, ForFamilia = true, ForMemey = true },
            new OperationType { Id = 3, Name = "Pago tarjeta", Kind = OperationKind.PagoTarjeta, Effect = CashEffect.Resta, ForFamilia = true, ForMemey = true },
            new OperationType { Id = 4, Name = "Cuota auto", Kind = OperationKind.Movimiento, Effect = CashEffect.Resta, ForFamilia = true, ForMemey = false },
            new OperationType { Id = 5, Name = "Ahorro", Kind = OperationKind.Ahorro, Effect = CashEffect.Resta, ForFamilia = true, ForMemey = true },
            new OperationType { Id = 6, Name = "Aporte a Memey", Kind = OperationKind.Transferencia, Effect = CashEffect.Resta, ForFamilia = true, ForMemey = false, TransferTo = Scope.Memey },
            new OperationType { Id = 7, Name = "Retiro de Memey", Kind = OperationKind.Transferencia, Effect = CashEffect.Resta, ForFamilia = false, ForMemey = true, TransferTo = Scope.Familia },
            new OperationType { Id = 8, Name = "Inversión", Kind = OperationKind.Inversion, Effect = CashEffect.Resta, ForFamilia = true, ForMemey = true },
            new OperationType { Id = 9, Name = "Retiro de ahorro", Kind = OperationKind.Ahorro, Effect = CashEffect.Suma, ForFamilia = true, ForMemey = true },
            new OperationType { Id = 10, Name = "Rescate de inversión", Kind = OperationKind.Inversion, Effect = CashEffect.Suma, ForFamilia = true, ForMemey = true });
    }
}
