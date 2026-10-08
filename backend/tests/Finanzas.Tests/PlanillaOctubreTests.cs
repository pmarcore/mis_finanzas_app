using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Finanzas.Tests;

/// <summary>
/// Importa la foto de octubre de la planilla (data/import/planilla-2026-10.json) en una base nueva
/// y compara contra los valores de la planilla al 08/10/2026.
/// </summary>
public class PlanillaOctubreTests : IClassFixture<PlanillaOctubreTests.Api>
{
    public class Api : WebApplicationFactory<Program>
    {
        readonly string dbPath = Path.Combine(Path.GetTempPath(), $"finanzas-test-{Guid.NewGuid():N}.db");
        public JsonElement Import = default;

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Finanzas", $"Data Source={dbPath}");

        public async Task EnsureImportedAsync()
        {
            if (Import.ValueKind != JsonValueKind.Undefined) return;
            var json = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "data", "import", "planilla-2026-10.json"));
            var res = await CreateClient().PostAsync("/import/sheet", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            Import = await res.Content.ReadFromJsonAsync<JsonElement>();
        }

        static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data", "import"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("No encuentro data/import");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    readonly Api api;
    public PlanillaOctubreTests(Api api) => this.api = api;

    async Task<JsonElement> Get(string url)
    {
        await api.EnsureImportedAsync();
        var res = await api.CreateClient().GetAsync(url);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    static decimal Num(JsonElement e, string prop) => e.GetProperty(prop).GetDecimal();

    [Fact]
    public async Task Import_loads_everything_without_warnings()
    {
        await api.EnsureImportedAsync();
        Assert.Equal(80, api.Import.GetProperty("transactions").GetInt32());
        Assert.Equal(7, api.Import.GetProperty("rules").GetInt32());
        Assert.Equal(0, api.Import.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Family_cash_at_cutoff_matches_the_sheet()
    {
        var d = await Get("/dashboard?scope=familia&date=2026-10-08");
        // Planilla, Dashboard: "Caja familiar al corte" $6.378.705 (redondeado).
        Assert.InRange(Num(d, "cashNow"), 6_378_704m, 6_378_705m);
        // Planilla, agenda de caja: saldo al 31/10 $734.857 (pagos de resumen previstos del Registro).
        Assert.InRange(Num(d, "projectedEndOfMonth"), 734_856m, 734_858m);
    }

    [Fact]
    public async Task Memey_cash_at_cutoff_with_opening_zero()
    {
        var d = await Get("/dashboard?scope=memey&date=2026-10-08");
        // 0 inicial − retiros 161.728 + seña 5.000 − insumos 35.879.
        Assert.Equal(-192_607m, Num(d, "cashNow"));
    }

    [Fact]
    public async Task November_card_payments_include_prior_installments_new_purchases_and_school()
    {
        var d = await Get("/dashboard?scope=total&date=2026-10-08");
        var nov = d.GetProperty("cardPayments").EnumerateArray().First(p => p.GetProperty("dueDate").GetString() == "2026-11-11");
        // Tarjetas: noviembre anterior $878.986 + nuevo $247.950 + Educación $1.654.000 (consumo del 20/10).
        // La planilla muestra las cuotas anteriores redondeadas; sumadas tal como se ven dan $878.988, de ahí los $2.
        Assert.Equal(2_780_938.04m, Num(nov, "total"));
    }

    [Fact]
    public async Task Budget_report_matches_the_sheet_actuals()
    {
        // La planilla cuenta como real también lo realizado el 09/10 (ABL y agua); con corte 09/10 coincide.
        var r = await Get("/reports/budget-vs-actual?month=2026-10&cutoff=2026-10-09");
        var rows = r.GetProperty("rows").EnumerateArray().ToDictionary(x => x.GetProperty("category").GetString()!);
        Assert.Equal(307_708m, Math.Round(Num(rows["Alimentos"], "actual")));
        Assert.Equal(284_407m, Math.Round(Num(rows["Servicios"], "actual")));
        Assert.Equal(240_706m, Num(rows["Extraordinarios"], "actual"));
        Assert.Equal(6_857m, Math.Round(Num(rows["Viaticos"], "actual")));
        Assert.Equal(50_000m, Num(rows["Auto operativo"], "actual"));
        Assert.Equal(1_255_952m, Math.Round(Num(r, "totalActual")));
        Assert.Equal(6_680_000m, Num(r, "totalBudget"));
    }

    [Fact]
    public async Task Budget_report_projection_and_alerts_at_the_8th()
    {
        var r = await Get("/reports/budget-vs-actual?month=2026-10&cutoff=2026-10-08");
        var rows = r.GetProperty("rows").EnumerateArray().ToDictionary(x => x.GetProperty("category").GetString()!);
        Assert.Equal(1_192_369m, Math.Round(Num(rows["Alimentos"], "projected")));
        Assert.Equal("Proyecta exceso", rows["Alimentos"].GetProperty("alert").GetString());
        Assert.Equal(932_736m, Math.Round(Num(rows["Extraordinarios"], "projected")));
        Assert.Equal("Critico", rows["Extraordinarios"].GetProperty("alert").GetString());
        Assert.Equal("Control", rows["Gimnasio"].GetProperty("alert").GetString());
        Assert.Equal(1_654_000m, Num(rows["Educacion"], "projected"));
    }

    [Fact]
    public async Task Future_months_subtract_the_budget_not_covered_by_fixed_expenses()
    {
        var d = await Get("/dashboard?scope=familia&date=2026-10-08");
        var est = d.GetProperty("estimatedMonthlySpend").EnumerateArray().First();
        // Presupuesto 6.680.000 − fijos cargados (1.654.000 + 1.509.000 + 700.000 + 112.000) = 2.705.000.
        Assert.Equal("2026-11", est.GetProperty("month").GetString());
        Assert.Equal(2_705_000m, Num(est, "amount"));
    }
}
