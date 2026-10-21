using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Finanzas.Domain;
using Finanzas.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Finanzas.Tests;

/// <summary>Dashboard de inversiones: lectura de IOL, totales, señales, fotos diarias y tenencias manuales de Ripio.</summary>
public class InversionesTests
{
    // Respuestas de ejemplo con la forma de la API v2 de IOL.
    const string PortfolioJson = """
        {"pais":"argentina","activos":[
          {"cantidad":100,"comprometido":0,"puntosVariacion":10,"variacionDiaria":2.5,"ultimoPrecio":5000,"ppc":4000,
           "gananciaPorcentaje":25,"gananciaDinero":100000,"valorizado":500000,
           "titulo":{"simbolo":"GGAL","descripcion":"Grupo Galicia","pais":"argentina","mercado":"bcba","tipo":"ACCIONES","plazo":"t1","moneda":"peso_Argentino"}},
          {"cantidad":10,"variacionDiaria":-1,"ultimoPrecio":20,"ppc":25,"gananciaPorcentaje":-20,"gananciaDinero":-50,"valorizado":200,
           "titulo":{"simbolo":"AL30D","descripcion":"Bono AL30 en dólares","mercado":"bcba","tipo":"TitulosPublicos","moneda":"dolar_Estadounidense"}},
          {"cantidad":0,"ultimoPrecio":1,"valorizado":0,"titulo":{"simbolo":"VACIO","tipo":"CEDEARS","moneda":"peso_Argentino"}}
        ]}
        """;

    const string AccountJson = """
        {"cuentas":[
          {"numero":"1","tipo":"inversion_Argentina_Pesos","moneda":"peso_Argentino","disponible":150000,"saldo":150000},
          {"numero":"1","tipo":"inversion_Argentina_Dolares","moneda":"dolar_Estadounidense","disponible":0,"saldo":0}
        ]}
        """;

    [Fact]
    public void IOL_positions_keep_the_broker_figures_as_they_come()
    {
        var activos = JsonDocument.Parse(PortfolioJson).RootElement.GetProperty("activos");
        var ggal = IolInvestmentProvider.ParsePosition(activos[0]);
        Assert.Equal(("GGAL", AssetType.Acciones, InvestmentCurrency.Ars), (ggal.Symbol, ggal.Type, ggal.Currency));
        Assert.Equal((25m, 100000m, 2.5m, 4000m, 500000m), (ggal.GainPct!.Value, ggal.GainAmount!.Value, ggal.DailyChangePct!.Value, ggal.AvgCost!.Value, ggal.MarketValue));

        var bond = IolInvestmentProvider.ParsePosition(activos[1]);
        Assert.Equal((AssetType.Bonos, InvestmentCurrency.Usd), (bond.Type, bond.Currency));

        var cash = IolInvestmentProvider.ParseCash(JsonDocument.Parse(AccountJson).RootElement).ToList();
        Assert.Single(cash);
        Assert.Equal((AssetType.Efectivo, InvestmentCurrency.Ars, 150000m), (cash[0].Type, cash[0].Currency, cash[0].MarketValue));

        Assert.Equal(AssetType.Cedears, IolInvestmentProvider.ParseType("CEDEARS"));
        Assert.Equal(AssetType.Ons, IolInvestmentProvider.ParseType("ObligacionesNegociables"));
        Assert.Equal(AssetType.Fci, IolInvestmentProvider.ParseType("FondoComundeInversion"));
    }

    [Fact]
    public async Task The_IOL_provider_logs_in_reads_portfolio_and_account_and_computes_MEP_from_AL30()
    {
        var handler = new FakeIol();
        var provider = new IolInvestmentProvider(new HttpClient(handler) { BaseAddress = new Uri("https://iol.test") }, new IolCredentials("u", "p"));

        var p = await provider.GetPortfolioAsync(Scope.Familia);
        Assert.NotNull(p);
        Assert.Equal(["GGAL", "AL30D", "ARS"], p!.Positions.Select(x => x.Symbol));
        Assert.Equal(1250m, p.Mep); // 75.000 / 60
        Assert.Null(await provider.GetPortfolioAsync(Scope.Memey));
        Assert.Equal(1, handler.Logins); // el token se reutiliza
        Assert.DoesNotContain(handler.Paths, x => x.Contains("operar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Totals_add_up_what_the_broker_reports_in_each_currency()
    {
        var positions = new List<InvestmentPosition>
        {
            new("invertirOnline", "GGAL", "", AssetType.Acciones, InvestmentCurrency.Ars, 100, 5000, 500_000, GainAmount: 100_000, GainPct: 25, DailyChangePct: 25),
            new("invertirOnline", "AL30D", "", AssetType.Bonos, InvestmentCurrency.Usd, 10, 20, 200, GainAmount: -50, GainPct: -20),
            new("invertirOnline", "ARS", "", AssetType.Efectivo, InvestmentCurrency.Ars, 150_000, 1, 150_000),
        };
        var ars = InvestmentMath.Totals(positions, InvestmentCurrency.Ars, 1000);
        Assert.Equal(850_000m, ars.Value);      // 500.000 + 200 × 1000 + 150.000
        Assert.Equal(50_000m, ars.Gain);        // 100.000 − 50 × 1000
        Assert.Equal(7.69m, ars.GainPct);       // 50.000 / (700.000 − 50.000)
        Assert.Equal(100_000m, ars.DailyChange); // 500.000 − 500.000 / 1,25
        Assert.Equal(150_000m, ars.Cash);

        var usd = InvestmentMath.Totals(positions, InvestmentCurrency.Usd, 1000);
        Assert.Equal(850m, usd.Value);
        Assert.Equal(50m, usd.Gain);
    }

    static readonly SignalSettings Defaults = new(25, 60, 5, 100_000, 100);

    [Fact]
    public void Concentration_flags_assets_types_and_currencies_over_their_limits()
    {
        var positions = new List<InvestmentPosition>
        {
            new("invertirOnline", "GGAL", "", AssetType.Acciones, InvestmentCurrency.Ars, 1, 1, 700),
            new("invertirOnline", "YPFD", "", AssetType.Acciones, InvestmentCurrency.Ars, 1, 1, 200),
            new("invertirOnline", "SPY", "", AssetType.Cedears, InvestmentCurrency.Ars, 1, 1, 100),
        };
        var s = InvestmentSignals.Compute(positions, 1000, new Dictionary<AssetType, decimal>(), Defaults, null);
        Assert.Contains(s, x => x.Kind == "concentracion" && x.Title.StartsWith("GGAL es el 70%"));
        Assert.DoesNotContain(s, x => x.Title.StartsWith("YPFD"));
        Assert.Contains(s, x => x.Title.StartsWith("Acciones son el 90%"));
        Assert.Contains(s, x => x.Title.Contains("está en pesos"));

        var spread = Enumerable.Range(0, 5).Select(i => new InvestmentPosition("invertirOnline", $"A{i}", "",
            (AssetType)i, i % 2 == 0 ? InvestmentCurrency.Ars : InvestmentCurrency.Usd, 1, 1, i % 2 == 0 ? 100 : 0.1m)).ToList();
        Assert.Empty(InvestmentSignals.Compute(spread, 1000, new Dictionary<AssetType, decimal>(), Defaults, null));
    }

    [Fact]
    public void Target_deviation_says_how_much_money_is_over_or_under_each_type()
    {
        var positions = new List<InvestmentPosition>
        {
            new("invertirOnline", "SPY", "", AssetType.Cedears, InvestmentCurrency.Ars, 1, 1, 380_000),
            new("invertirOnline", "AL30", "", AssetType.Bonos, InvestmentCurrency.Ars, 1, 1, 310_000),
            new("invertirOnline", "GGAL", "", AssetType.Acciones, InvestmentCurrency.Ars, 1, 1, 160_000),
            new("ripio", "BTC", "", AssetType.Cripto, InvestmentCurrency.Ars, 1, 1, 100_000),
            new("invertirOnline", "ARS", "", AssetType.Efectivo, InvestmentCurrency.Ars, 1, 1, 50_000),
        };
        var targets = new Dictionary<AssetType, decimal> { [AssetType.Bonos] = 40, [AssetType.Cedears] = 30, [AssetType.Acciones] = 15, [AssetType.Cripto] = 10, [AssetType.Efectivo] = 5 };
        var s = InvestmentSignals.Compute(positions, 1000, targets, Defaults with { MaxAssetPct = 100, MaxGroupPct = 100 }, null)
            .Where(x => x.Kind == "objetivo").ToList();
        Assert.Equal(2, s.Count); // acciones (+1), cripto (0) y efectivo (0) quedan dentro de 5 puntos
        Assert.Contains(s, x => x.Title == "CEDEARs: 38% (objetivo 30%)" && x.Detail.StartsWith("$80.000 por encima"));
        Assert.Contains(s, x => x.Title == "Bonos: 31% (objetivo 40%)" && x.Detail.StartsWith("Faltan $90.000"));
    }

    [Fact]
    public void Idle_cash_shows_broker_balance_over_the_threshold_and_projected_cash_surplus()
    {
        var positions = new List<InvestmentPosition>
        {
            new("invertirOnline", "ARS", "", AssetType.Efectivo, InvestmentCurrency.Ars, 1, 1, 850_000),
            new("invertirOnline", "USD", "", AssetType.Efectivo, InvestmentCurrency.Usd, 1, 1, 50),
        };
        var s = InvestmentSignals.Compute(positions, 1000, new Dictionary<AssetType, decimal>(), Defaults with { MaxGroupPct = 100, MaxAssetPct = 100 }, 400_000)
            .Where(x => x.Kind == "plataQuieta").ToList();
        Assert.Equal(2, s.Count);
        Assert.Contains(s, x => x.Title == "Tenés $850.000 sin invertir en IOL");
        Assert.Contains(s, x => x.Title.EndsWith("por $400.000"));
    }

    // ---------- Por la API, con proveedores falsos ----------

    public class Api(FakeProvider provider) : WebApplicationFactory<Program>
    {
        readonly string dbPath = Path.Combine(Path.GetTempPath(), $"finanzas-inv-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Finanzas", $"Data Source={dbPath}");
            builder.ConfigureTestServices(s =>
            {
                s.AddSingleton<IInvestmentProvider>(provider);
                s.AddSingleton<ICryptoPrices>(new FakeCrypto());
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Dashboard_shows_IOL_and_Ripio_saves_one_snapshot_per_day_and_falls_back_when_IOL_fails()
    {
        var provider = new FakeProvider();
        using var api = new Api(provider);
        var c = api.CreateClient();

        var btc = await Json(await c.PostAsJsonAsync("/investments/manual", new { symbol = "btc", quantity = 0.01m, costUsd = 500m }));
        Assert.Equal("BTC", btc.GetProperty("symbol").GetString());

        var d = await Json(await c.GetAsync("/investments/dashboard"));
        Assert.False(d.GetProperty("stale").GetBoolean());
        Assert.Equal(1000m, d.GetProperty("mep").GetDecimal());
        var holdings = d.GetProperty("holdings").EnumerateArray().ToList();
        var ggal = holdings.Single(h => h.GetProperty("symbol").GetString() == "GGAL");
        Assert.Equal(25m, ggal.GetProperty("gainPct").GetDecimal());
        Assert.False(ggal.GetProperty("gainIsComputed").GetBoolean());
        var ripio = holdings.Single(h => h.GetProperty("symbol").GetString() == "BTC");
        Assert.Equal(600m, ripio.GetProperty("marketValue").GetDecimal()); // 0,01 × 60.000
        Assert.Equal(20m, ripio.GetProperty("gainPct").GetDecimal());     // (600 − 500) / 500
        Assert.True(ripio.GetProperty("gainIsComputed").GetBoolean());
        Assert.Equal(1_100_000m, d.GetProperty("ars").GetProperty("value").GetDecimal()); // 500.000 + 600 × 1000
        Assert.Contains(d.GetProperty("bySource").EnumerateArray(), s => s.GetProperty("label").GetString() == "Ripio");
        Assert.Contains(d.GetProperty("quotes").EnumerateArray(), q => q.GetProperty("symbol").GetString() == "SPY");

        // Dentro de los 15 minutos no vuelve a llamar a IOL; Actualizar sí, y la foto del día se reemplaza.
        await Json(await c.GetAsync("/investments/dashboard"));
        Assert.Equal(1, provider.Calls);
        await Json(await c.PostAsync("/investments/sync", null));
        Assert.Equal(2, provider.Calls);
        var history = (await Json(await c.GetAsync("/investments/history"))).EnumerateArray().ToList();
        Assert.Single(history);
        Assert.Equal(1_100_000m, history[0].GetProperty("ars").GetDecimal());

        var savings = await Json(await c.GetAsync("/savings?scope=familia"));
        Assert.Equal(1_100_000m, savings.GetProperty("inversion").GetProperty("marketValue").GetDecimal());

        provider.Fail = true;
        var stale = await Json(await c.GetAsync("/investments/dashboard?refresh=true"));
        Assert.True(stale.GetProperty("stale").GetBoolean());
        Assert.Contains("IOL", stale.GetProperty("error").GetString());
        Assert.Equal(1_100_000m, stale.GetProperty("ars").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Targets_must_add_up_to_100_and_settings_validate_ranges()
    {
        using var api = new Api(new FakeProvider());
        var c = api.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/investments/targets", new[] { new { assetType = "bonos", targetPct = 60 } })).StatusCode);
        await Json(await c.PutAsJsonAsync("/investments/targets", new[] { new { assetType = "bonos", targetPct = 60 }, new { assetType = "cedears", targetPct = 40 } }));
        Assert.Equal(2, (await Json(await c.GetAsync("/investments/targets"))).GetArrayLength());
        var d = await Json(await c.GetAsync("/investments/dashboard"));
        Assert.True(d.GetProperty("targetsConfigured").GetBoolean());
        Assert.Contains(d.GetProperty("signals").EnumerateArray(), s => s.GetProperty("kind").GetString() == "objetivo");

        var settings = await Json(await c.GetAsync("/investments/settings"));
        Assert.Equal(25m, settings.GetProperty("maxAssetPct").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/investments/settings",
            new { maxAssetPct = 0, maxGroupPct = 60, targetDeviationPts = 5, idleCashArs = 0, idleCashUsd = 0, watchList = "" })).StatusCode);
    }

    [Fact]
    public void Watch_list_entries_default_to_bcba_and_use_IOL_market_names()
    {
        Assert.Equal([("bCBA", "GGAL"), ("nYSE", "SPY"), ("cripto", "BTC")], Finanzas.Api.InvestmentService.ParseWatchList(" ggal , nyse:spy,cripto:btc,"));
    }

    // ---------- Falsos ----------

    public class FakeProvider : IInvestmentProvider
    {
        public bool Fail;
        public int Calls;
        public string Name => "invertirOnline";
        public bool IsConnected => true;

        public Task<InvestmentPortfolio?> GetPortfolioAsync(Scope scope, CancellationToken ct = default)
        {
            Calls++;
            if (Fail) throw new InvestmentProviderException("IOL respondió 500.");
            return Task.FromResult<InvestmentPortfolio?>(new InvestmentPortfolio(DateTimeOffset.Now,
            [
                new("invertirOnline", "GGAL", "Grupo Galicia", AssetType.Acciones, InvestmentCurrency.Ars, 100, 5000, 500_000,
                    AvgCost: 4000, DailyChangePct: 2.5m, GainPct: 25, GainAmount: 100_000, Market: "bcba"),
            ], 1000));
        }

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<(string Market, string Symbol)> items, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quote>>(items.Select(i => new Quote(i.Symbol, i.Market, InvestmentCurrency.Ars, 100, 1)).ToList());

        public Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string market, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PricePoint>>([new(from, 1), new(to, 2)]);
    }

    class FakeCrypto : ICryptoPrices
    {
        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quote>>(symbols.Select(s => new Quote(s.ToUpperInvariant(), "cripto", InvestmentCurrency.Usd, 60_000, -1)).ToList());

        public Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string symbol, int days, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PricePoint>>([]);
    }

    class FakeIol : HttpMessageHandler
    {
        public int Logins;
        public List<string> Paths = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/token")
            {
                Logins++;
                return Ok("""{"access_token":"t","refresh_token":"r","expires_in":900}""");
            }
            if (request.Headers.Authorization?.Parameter != "t") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return path switch
            {
                "/api/v2/portafolio/argentina" => Ok(PortfolioJson),
                "/api/v2/portafolio/estados_Unidos" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                "/api/v2/estadocuenta" => Ok(AccountJson),
                "/api/v2/bCBA/Titulos/AL30/Cotizacion" => Ok("""{"ultimoPrecio":75000,"variacion":1,"moneda":"peso_Argentino"}"""),
                "/api/v2/bCBA/Titulos/AL30D/Cotizacion" => Ok("""{"ultimoPrecio":60,"variacion":0.5,"moneda":"dolar_Estadounidense"}"""),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            };
        }

        static Task<HttpResponseMessage> Ok(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
