using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finanzas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CuotasPorResumen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FirstStatementMonth",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StatementPinned",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StatementMonth",
                table: "CashImpacts",
                type: "TEXT",
                nullable: true);

            // Cada cuota y cada pago quedan en el resumen del mes de la fecha que tenían (así las cuotas al 11/12 y los fijos al
            // 04/12 pasan a ser un solo resumen de diciembre). Al arrancar, la API recalcula resumen por fecha de compra y fechas
            // de vencimiento con los cierres cargados (CardStatementSync).
            migrationBuilder.Sql("UPDATE CashImpacts SET StatementMonth = date(ImpactDate, 'start of month') WHERE CardId IS NOT NULL;");
            migrationBuilder.Sql("UPDATE Transactions SET FirstStatementMonth = date(FirstDueDate, 'start of month') WHERE FirstDueDate IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstStatementMonth",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "StatementPinned",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "StatementMonth",
                table: "CashImpacts");
        }
    }
}
