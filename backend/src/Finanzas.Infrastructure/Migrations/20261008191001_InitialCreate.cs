using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Finanzas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashBoxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OpeningBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ObservedBalance = table.Column<long>(type: "INTEGER", nullable: true),
                    ObservedDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBoxes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    BudgetType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    CashBoxId = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningCardDebt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Operation = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndMonth = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CashFloor = table.Column<long>(type: "INTEGER", nullable: false),
                    SavingsGoal = table.Column<long>(type: "INTEGER", nullable: false),
                    AlertAttentionPct = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertControlPct = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertExceededPct = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertCriticalPct = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Statements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PaymentMethodId = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ConfirmedAmount = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Operation = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "INTEGER", nullable: false),
                    Installments = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstDueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RecurringRuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    PaidCardId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsPriorCommitment = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PaymentMethodId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClosingDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardCycles_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringRuleAmounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecurringRuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRuleAmounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringRuleAmounts_RecurringRules_RecurringRuleId",
                        column: x => x.RecurringRuleId,
                        principalTable: "RecurringRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CashImpacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    CashBoxId = table.Column<int>(type: "INTEGER", nullable: false),
                    CardId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImpactDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    InstallmentNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashImpacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashImpacts_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CashBoxes",
                columns: new[] { "Id", "Name", "ObservedBalance", "ObservedDate", "OpeningBalance", "OpeningDate", "Scope" },
                values: new object[,]
                {
                    { 1, "Caja familiar consolidada", null, null, -14346400L, new DateOnly(2026, 10, 1), 1 },
                    { 2, "Memey Mercado Pago", null, null, 0L, new DateOnly(2026, 10, 1), 2 }
                });

            migrationBuilder.InsertData(
                table: "PaymentMethods",
                columns: new[] { "Id", "CashBoxId", "Name", "OpeningCardDebt", "Type" },
                values: new object[,]
                {
                    { 1, 1, "Débito / transferencia", 0L, 1 },
                    { 2, 1, "Efectivo", 0L, 2 },
                    { 3, 2, "Memey Mercado Pago", 0L, 3 },
                    { 4, 1, "Visa Santander", 0L, 4 },
                    { 5, 1, "Visa Galicia", 0L, 4 },
                    { 6, 1, "Master Galicia", 0L, 4 },
                    { 7, 1, "Tarjeta Mercado Pago", 0L, 4 }
                });

            migrationBuilder.InsertData(
                table: "People",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Pablo" },
                    { 2, "Rocío" },
                    { 3, "Familia" }
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "AlertAttentionPct", "AlertControlPct", "AlertCriticalPct", "AlertExceededPct", "CashFloor", "SavingsGoal" },
                values: new object[] { 1, 70, 80, 120, 100, 30000000L, 50000000L });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_Month_CategoryId",
                table: "Budgets",
                columns: new[] { "Month", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardCycles_PaymentMethodId_DueDate",
                table: "CardCycles",
                columns: new[] { "PaymentMethodId", "DueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashBoxes_Scope",
                table: "CashBoxes",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashImpacts_ImpactDate",
                table: "CashImpacts",
                column: "ImpactDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashImpacts_TransactionId",
                table: "CashImpacts",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRuleAmounts_RecurringRuleId",
                table: "RecurringRuleAmounts",
                column: "RecurringRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Statements_PaymentMethodId_DueDate",
                table: "Statements",
                columns: new[] { "PaymentMethodId", "DueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Date",
                table: "Transactions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportId",
                table: "Transactions",
                column: "ImportId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "CardCycles");

            migrationBuilder.DropTable(
                name: "CashBoxes");

            migrationBuilder.DropTable(
                name: "CashImpacts");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "RecurringRuleAmounts");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Statements");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "RecurringRules");
        }
    }
}
