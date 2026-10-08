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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CashBox>().HasIndex(x => x.Scope).IsUnique();

        b.Entity<PaymentMethod>().HasMany(x => x.Cycles).WithOne().HasForeignKey(c => c.PaymentMethodId);
        b.Entity<PaymentMethod>().Ignore(x => x.IsCard);
        b.Entity<CardCycle>().HasIndex(x => new { x.PaymentMethodId, x.DueDate }).IsUnique();

        b.Entity<Transaction>().HasMany(x => x.Impacts).WithOne().HasForeignKey(i => i.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Transaction>().HasIndex(x => x.Date);
        b.Entity<Transaction>().HasIndex(x => x.ImportId).IsUnique();
        b.Entity<CashImpact>().HasIndex(x => x.ImpactDate);

        b.Entity<Statement>().HasIndex(x => new { x.PaymentMethodId, x.DueDate }).IsUnique();

        b.Entity<RecurringRule>().HasMany(x => x.Amounts).WithOne().HasForeignKey(a => a.RecurringRuleId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Budget>().HasIndex(x => new { x.Month, x.CategoryId }).IsUnique();

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
    }
}
