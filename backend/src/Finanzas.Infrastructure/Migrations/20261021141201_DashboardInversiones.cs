using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finanzas.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DashboardInversiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestmentSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaxAssetPct = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxGroupPct = table.Column<decimal>(type: "TEXT", nullable: false),
                    TargetDeviationPts = table.Column<decimal>(type: "TEXT", nullable: false),
                    IdleCashArs = table.Column<long>(type: "INTEGER", nullable: false),
                    IdleCashUsd = table.Column<long>(type: "INTEGER", nullable: false),
                    WatchList = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AssetType = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<int>(type: "INTEGER", nullable: false),
                    Market = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    LastPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    AvgCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    DailyChangePct = table.Column<decimal>(type: "TEXT", nullable: true),
                    GainPct = table.Column<decimal>(type: "TEXT", nullable: true),
                    GainAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    GainIsComputed = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarketValue = table.Column<decimal>(type: "TEXT", nullable: false),
                    MarketValueArs = table.Column<long>(type: "INTEGER", nullable: false),
                    MarketValueUsd = table.Column<long>(type: "INTEGER", nullable: false),
                    Mep = table.Column<decimal>(type: "TEXT", nullable: true),
                    TakenAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetType = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetPct = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualHoldings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    CostUsd = table.Column<decimal>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualHoldings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "InvestmentSettings",
                columns: new[] { "Id", "IdleCashArs", "IdleCashUsd", "MaxAssetPct", "MaxGroupPct", "TargetDeviationPts", "WatchList" },
                values: new object[] { 1, 10000000L, 10000L, 25m, 60m, 5m, "GGAL,nyse:SPY,cripto:BTC" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentSnapshots_Date_Source_Symbol",
                table: "InvestmentSnapshots",
                columns: new[] { "Date", "Source", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentTargets_AssetType",
                table: "InvestmentTargets",
                column: "AssetType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentSettings");

            migrationBuilder.DropTable(
                name: "InvestmentSnapshots");

            migrationBuilder.DropTable(
                name: "InvestmentTargets");

            migrationBuilder.DropTable(
                name: "ManualHoldings");
        }
    }
}
