using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Finanzas.Domain;

namespace Finanzas.Infrastructure;

/// <summary>Proveedor por defecto mientras Invertir Online no tenga credenciales: no informa nada.</summary>
public class SinProveedorDeInversiones : IInvestmentProvider
{
    public string Name => "invertirOnline";
    public bool IsConnected => false;
    public Task<InvestmentPortfolio?> GetPortfolioAsync(Scope scope, CancellationToken ct = default) => Task.FromResult<InvestmentPortfolio?>(null);
    public Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<(string Market, string Symbol)> items, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Quote>>([]);
    public Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string market, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PricePoint>>([]);
}

public record IolCredentials(string Username, string Password);

/// <summary>
/// Invertir Online por su API oficial (api.invertironline.com), solo lectura: token, portafolio, estado de cuenta y
/// cotizaciones. Nunca llama a endpoints de operaciones. El token y el refresh token viven solo en memoria. Las
/// inversiones son de Familia: para Memey devuelve null.
/// </summary>
public class IolInvestmentProvider(HttpClient http, IolCredentials credentials) : IInvestmentProvider
{
    public const string SourceName = "invertirOnline";
    public string Name => SourceName;
    public bool IsConnected => true;

    readonly SemaphoreSlim tokenLock = new(1, 1);
    string? accessToken, refreshToken;
    DateTimeOffset accessExpires, refreshExpires;

    public async Task<InvestmentPortfolio?> GetPortfolioAsync(Scope scope, CancellationToken ct = default)
    {
        if (scope != Scope.Familia) return null;
        var positions = new List<InvestmentPosition>();
        foreach (var country in new[] { "argentina", "estados_Unidos" })
        {
            using var doc = await GetJsonAsync($"/api/v2/portafolio/{country}", ct, optional: country != "argentina");
            if (doc is null || !doc.RootElement.TryGetProperty("activos", out var activos)) continue;
            positions.AddRange(activos.EnumerateArray().Select(ParsePosition).Where(p => p.Quantity != 0));
        }

        using (var account = await GetJsonAsync("/api/v2/estadocuenta", ct))
            positions.AddRange(ParseCash(account!.RootElement));

        var quotes = await GetQuotesAsync([("bCBA", "AL30"), ("bCBA", "AL30D")], ct);
        var mep = InvestmentMath.Mep(quotes.FirstOrDefault(q => q.Symbol == "AL30")?.LastPrice, quotes.FirstOrDefault(q => q.Symbol == "AL30D")?.LastPrice);
        return new InvestmentPortfolio(DateTimeOffset.Now, positions, mep);
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<(string Market, string Symbol)> items, CancellationToken ct = default)
    {
        var quotes = new List<Quote>();
        foreach (var (market, symbol) in items)
        {
            using var doc = await GetJsonAsync($"/api/v2/{Uri.EscapeDataString(market)}/Titulos/{Uri.EscapeDataString(symbol)}/Cotizacion", ct, optional: true);
            if (doc is null) continue;
            var q = doc.RootElement;
            quotes.Add(new Quote(symbol, market, ParseCurrency(Str(q, "moneda")), Dec(q, "ultimoPrecio") ?? 0, Dec(q, "variacion"), Str(q, "descripcionTitulo")));
        }
        return quotes;
    }

    public async Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string market, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync(
            $"/api/v2/{Uri.EscapeDataString(market)}/Titulos/{Uri.EscapeDataString(symbol)}/Cotizacion/seriehistorica/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}/ajustada", ct, optional: true);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        return doc.RootElement.EnumerateArray()
            .Select(e => (date: DateTime.TryParse(Str(e, "fechaHora"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? DateOnly.FromDateTime(d) : (DateOnly?)null, price: Dec(e, "ultimoPrecio")))
            .Where(x => x.date is not null && x.price is not null)
            .GroupBy(x => x.date!.Value)
            .Select(g => new PricePoint(g.Key, g.First().price!.Value))
            .OrderBy(p => p.Date)
            .ToList();
    }

    // ---------- Lectura de las respuestas de IOL (públicas para poder probarlas con JSON de ejemplo) ----------

    public static InvestmentPosition ParsePosition(JsonElement a)
    {
        var t = a.GetProperty("titulo");
        return new InvestmentPosition(
            SourceName, Str(t, "simbolo") ?? "?", Str(t, "descripcion") ?? "", ParseType(Str(t, "tipo")), ParseCurrency(Str(t, "moneda")),
            Dec(a, "cantidad") ?? 0, Dec(a, "ultimoPrecio") ?? 0, Dec(a, "valorizado") ?? 0,
            AvgCost: Dec(a, "ppc"), DailyChangePct: Dec(a, "variacionDiaria"), GainPct: Dec(a, "gananciaPorcentaje"),
            GainAmount: Dec(a, "gananciaDinero"), Market: Str(t, "mercado"));
    }

    /// <summary>Saldo disponible sin invertir, una tenencia de tipo Efectivo por moneda.</summary>
    public static IEnumerable<InvestmentPosition> ParseCash(JsonElement account)
    {
        if (!account.TryGetProperty("cuentas", out var cuentas)) yield break;
        foreach (var g in cuentas.EnumerateArray().GroupBy(c => ParseCurrency(Str(c, "moneda"))))
        {
            var available = g.Sum(c => Dec(c, "disponible") ?? 0);
            if (available == 0) continue;
            var symbol = g.Key == InvestmentCurrency.Ars ? "ARS" : "USD";
            yield return new InvestmentPosition(SourceName, symbol, g.Key == InvestmentCurrency.Ars ? "Pesos disponibles" : "Dólares disponibles",
                AssetType.Efectivo, g.Key, available, 1, available);
        }
    }

    public static AssetType ParseType(string? tipo)
    {
        var t = (tipo ?? "").ToLowerInvariant();
        if (t.Contains("cedear")) return AssetType.Cedears;
        if (t.Contains("accion")) return AssetType.Acciones;
        if (t.Contains("obligacion")) return AssetType.Ons;
        if (t.Contains("publico") || t.Contains("letra") || t.Contains("bono")) return AssetType.Bonos;
        if (t.Contains("fondo") || t.Contains("fci")) return AssetType.Fci;
        return AssetType.Otros;
    }

    public static InvestmentCurrency ParseCurrency(string? moneda) =>
        (moneda ?? "").Contains("dolar", StringComparison.OrdinalIgnoreCase) || string.Equals(moneda, "USD", StringComparison.OrdinalIgnoreCase)
            ? InvestmentCurrency.Usd : InvestmentCurrency.Ars;

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static decimal? Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    // ---------- HTTP y token ----------

    async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken ct, bool optional = false)
    {
        var token = await TokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            if (optional) return null;
            throw new InvestmentProviderException($"IOL respondió {(int)res.StatusCode} en {path.Split('?')[0]}.");
        }
        return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    async Task<string> TokenAsync(CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            if (accessToken is not null && DateTimeOffset.UtcNow < accessExpires) return accessToken;
            if (refreshToken is not null && DateTimeOffset.UtcNow < refreshExpires
                && await RequestTokenAsync(new() { ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token" }, ct))
                return accessToken!;
            // Nunca se incluye el cuerpo de la respuesta ni las credenciales en el mensaje.
            if (!await RequestTokenAsync(new() { ["username"] = credentials.Username, ["password"] = credentials.Password, ["grant_type"] = "password" }, ct))
                throw new InvestmentProviderException("IOL rechazó el inicio de sesión. Revisá usuario, contraseña y que la API esté habilitada en tu cuenta.");
            return accessToken!;
        }
        finally { tokenLock.Release(); }
    }

    async Task<bool> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        using var res = await http.PostAsync("/token", new FormUrlEncodedContent(form), ct);
        if (!res.IsSuccessStatusCode) { accessToken = refreshToken = null; return false; }
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        accessToken = body.GetProperty("access_token").GetString();
        refreshToken = Str(body, "refresh_token");
        var expiresIn = body.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 900;
        accessExpires = now.AddSeconds(Math.Max(30, expiresIn - 60));
        refreshExpires = DateTimeOffset.TryParse(Str(body, ".refreshexpires"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var r) ? r : now.AddMinutes(30);
        return accessToken is not null;
    }
}

public class InvestmentProviderException(string message) : Exception(message);

/// <summary>Precios de cripto en USD (contra USDT) desde la API pública de Binance, sin clave.</summary>
public class BinanceCryptoPrices(HttpClient http) : ICryptoPrices
{
    static readonly HashSet<string> Dollars = ["USDT", "USDC", "DAI", "USD"];

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var quotes = new List<Quote>();
        foreach (var symbol in symbols.Select(s => s.ToUpperInvariant()).Distinct())
        {
            if (Dollars.Contains(symbol)) { quotes.Add(new Quote(symbol, "cripto", InvestmentCurrency.Usd, 1, 0)); continue; }
            using var res = await http.GetAsync($"/api/v3/ticker/24hr?symbol={Uri.EscapeDataString(symbol)}USDT", ct);
            if (!res.IsSuccessStatusCode) continue;
            var t = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (decimal.TryParse(t.GetProperty("lastPrice").GetString(), CultureInfo.InvariantCulture, out var price))
                quotes.Add(new Quote(symbol, "cripto", InvestmentCurrency.Usd, price,
                    decimal.TryParse(t.GetProperty("priceChangePercent").GetString(), CultureInfo.InvariantCulture, out var pct) ? pct : null));
        }
        return quotes;
    }

    public async Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string symbol, int days, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        if (Dollars.Contains(symbol)) return [];
        using var res = await http.GetAsync($"/api/v3/klines?symbol={Uri.EscapeDataString(symbol)}USDT&interval=1d&limit={Math.Clamp(days, 1, 1000)}", ct);
        if (!res.IsSuccessStatusCode) return [];
        var rows = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        return rows.EnumerateArray()
            .Select(r => new PricePoint(
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(r[0].GetInt64()).UtcDateTime),
                decimal.Parse(r[4].GetString()!, CultureInfo.InvariantCulture)))
            .ToList();
    }
}
