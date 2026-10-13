using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Finanzas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TiposDeOperacionAhorroInversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Operation",
                table: "Transactions",
                newName: "OperationTypeId");

            migrationBuilder.RenameColumn(
                name: "Operation",
                table: "RecurringRules",
                newName: "OperationTypeId");

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "PaymentMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "InvestmentValuations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MarketValue = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentValuations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Effect = table.Column<int>(type: "INTEGER", nullable: false),
                    ForFamilia = table.Column<bool>(type: "INTEGER", nullable: false),
                    ForMemey = table.Column<bool>(type: "INTEGER", nullable: false),
                    TransferTo = table.Column<int>(type: "INTEGER", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationTypes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "OperationTypes",
                columns: new[] { "Id", "Active", "Effect", "ForFamilia", "ForMemey", "Kind", "Name", "TransferTo" },
                values: new object[,]
                {
                    { 1, true, 1, true, true, 1, "Ingreso", null },
                    { 2, true, -1, true, true, 1, "Gasto", null },
                    { 3, true, -1, true, true, 2, "Pago tarjeta", null },
                    { 4, true, -1, true, false, 1, "Cuota auto", null },
                    { 5, true, -1, true, true, 4, "Ahorro", null },
                    { 6, true, -1, true, false, 3, "Aporte a Memey", 2 },
                    { 7, true, -1, false, true, 3, "Retiro de Memey", 1 },
                    { 8, true, -1, true, true, 5, "Inversión", null },
                    { 9, true, 1, true, true, 4, "Retiro de ahorro", null },
                    { 10, true, 1, true, true, 5, "Rescate de inversión", null }
                });

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 1,
                column: "Active",
                value: true);

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 2,
                column: "Active",
                value: true);

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 3,
                column: "Active",
                value: true);

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 4,
                column: "Active",
                value: true);

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 5,
                column: "Active",
                value: true);

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 6,
                column: "Active",
                value: true);

            migrationBuilder.UpdateData(
                table: "PaymentMethods",
                keyColumn: "Id",
                keyValue: 7,
                column: "Active",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OperationTypeId",
                table: "Transactions",
                column: "OperationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_OperationTypeId",
                table: "RecurringRules",
                column: "OperationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentValuations_Scope_Date",
                table: "InvestmentValuations",
                columns: new[] { "Scope", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationTypes_Name",
                table: "OperationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringRules_OperationTypes_OperationTypeId",
                table: "RecurringRules",
                column: "OperationTypeId",
                principalTable: "OperationTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_OperationTypes_OperationTypeId",
                table: "Transactions",
                column: "OperationTypeId",
                principalTable: "OperationTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringRules_OperationTypes_OperationTypeId",
                table: "RecurringRules");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_OperationTypes_OperationTypeId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "InvestmentValuations");

            migrationBuilder.DropTable(
                name: "OperationTypes");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_OperationTypeId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringRules_OperationTypeId",
                table: "RecurringRules");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "PaymentMethods");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Categories");

            migrationBuilder.RenameColumn(
                name: "OperationTypeId",
                table: "Transactions",
                newName: "Operation");

            migrationBuilder.RenameColumn(
                name: "OperationTypeId",
                table: "RecurringRules",
                newName: "Operation");
        }
    }
}
