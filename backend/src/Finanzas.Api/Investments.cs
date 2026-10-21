using Finanzas.Domain;
using Finanzas.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Finanzas.Api;

// Dashboard de inversiones (Spec/inversiones-dashboard.md). Solo Familia. Los rendimientos por activo son los del broker.

public record TotalsDto(decimal Value, decimal Gain, decimal? GainPct, decimal DailyChange, decimal? DailyChangePct, decimal Cash)
{
    public static TotalsDto From(PortfolioTotals t) =>
        new(R(t.Value), R(t.Gain), t.GainPct, R(t.DailyChange), t.DailyChangePct, R(t.Cash));
    static decimal R(decimal v) => Math.Round(v, 2);
}

public record SliceDto(string Key, string Label, decimal Ars, decimal Usd, decimal Pct);

public record HoldingDto(
    string Source, string Symbol, string Description, AssetType Type, InvestmentCurrency Currency, string? Market,
    decimal Quantity, decimal LastPrice, decimal? AvgCost, decimal? DailyChangePct, decimal? GainPct, decimal? GainAmount,
    bool GainIsComputed, decimal MarketValue, decimal ValueArs, decimal ValueUsd, decimal Pct);

public record QuoteDto(string Symbol, string Market, InvestmentCurrency Currency, decimal LastPrice, decimal? DailyChangePct, string? Description);

public record InvestmentDashboardDto(
    DateTimeOffset? AsOf, bool Stale, string? Error, bool ProviderConnected, decimal? Mep,
    TotalsDto Ars, TotalsDto Usd, List<SliceDto> ByType, List<SliceDto> ByCurrency, List<SliceDto> BySource,
    List<HoldingDto> Holdings, List<QuoteDto> Quotes, List<InvestmentSignal> Signals, bool TargetsConfigured);

public record HistoryPointDto(DateOnly Date, decimal Ars, decimal Usd);

public record ManualHoldingDto(int Id, string Source, string Symbol, decimal Quantity, decimal? CostUsd, bool Active)
{
    public static ManualHoldingDto From(ManualHolding h) => new(h.Id, h.Source, h.Symbol, h.Quantity, h.CostUsd, h.Active);
}

public record SaveManualHoldingDto(string Symbol, decimal Quantity, decimal? CostUsd, bool Active = true);

public record InvestmentTargetDto(AssetType AssetType, decimal TargetPct);

public record InvestmentSettingsDto(decimal MaxAssetPct, decimal MaxGroupPct, decimal TargetDeviationPts, decimal IdleCashArs, decimal IdleCashUsd, string WatchList)
{
    public static InvestmentSettingsDto From(InvestmentSettings s) =>
        new(s.MaxAssetPct, s.MaxGroupPct, s.TargetDeviationPts, Money.Pesos(s.IdleCashArs), Money.Pesos(s.IdleCashUsd), s.WatchList);
}

/// <summary>
/// Junta las tenencias de IOL y las manuales de Ripio, guarda la foto del día y arma el dashboard. Lo traído en vivo se
/// guarda 15 minutos; si IOL no responde se muestra la última foto guardada.
/// </summary>
public class InvestmentService(FinanzasDbContext db, IInvestmentProvider provider, ICryptoPrices crypto, IMemoryCache cache)
{
    const string CacheKey = "investments:live";
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    record Live(DateTimeOffset AsOf, List<InvestmentPosition> Positions, decimal? Mep, List<Quote> Quotes);

    public async Task<InvestmentDashboardDto> DashboardAsync(bool refresh, CancellationToken ct)
    {
        string? error = null;
        var live = refresh ? null : cache.Get<Live>(CacheKey);
        var stale = false;
        if (live is null)
        {
            try
            {
                live = await FetchAsync(ct);
                cache.Set(CacheKey, live, CacheFor);
            }
            catch (Exception e) when (e is InvestmentProviderException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                error = e is InvestmentProviderException ? e.Message : "No se pudo conectar con el broker.";
                live = await LastSnapshotAsync(ct);
                stale = true;
            }
        }
        return await BuildAsync(live, stale, error, ct);
    }

    async Task<Live> FetchAsync(CancellationToken ct)
    {
        var positions = new List<InvestmentPosition>();
        decimal? mep = null;
        if (provider.IsConnected)
        {
            var portfolio = await provider.GetPortfolioAsync(Scope.Familia, ct);
            if (portfolio is not null) { positions.AddRange(portfolio.Positions); mep = portfolio.Mep; }
        }
        mep ??= await db.InvestmentSnapshots.Where(s => s.Mep != null).OrderByDescending(s => s.Date).Select(s => s.Mep).FirstOrDefaultAsync(ct);

        var holdings = await db.ManualHoldings.Where(h => h.Active).ToListAsync(ct);
        var settings = await db.InvestmentSettings.FirstAsync(ct);
        var watch = ParseWatchList(settings.WatchList);
        var cryptoQuotes = await SafeAsync(() => crypto.GetQuotesAsync(holdings.Select(h => h.Symbol).Concat(watch.Where(w => w.Market == "cripto").Select(w => w.Symbol)), ct));
        foreach (var h in holdings)
        {
            var q = cryptoQuotes.FirstOrDefault(q => q.Symbol.Equals(h.Symbol, StringComparison.OrdinalIgnoreCase));
            var price = q?.LastPrice ?? 0;
            var value = h.Quantity * price;
            decimal? gain = h.CostUsd is > 0 && q is not null ? value - h.CostUsd.Value : null;
            positions.Add(new InvestmentPosition(h.Source, h.Symbol.ToUpperInvariant(), $"{h.Symbol.ToUpperInvariant()} en Ripio", AssetType.Cripto, InvestmentCurrency.Usd,
                h.Quantity, price, value, AvgCost: h.CostUsd is > 0 && h.Quantity > 0 ? h.CostUsd / h.Quantity : null, DailyChangePct: q?.DailyChangePct,
                GainPct: gain is not null ? Math.Round(gain.Value / h.CostUsd!.Value * 100, 2) : null, GainAmount: gain, GainIsComputed: gain is not null, Market: "cripto"));
        }

        var quotes = new List<Quote>();
        quotes.AddRange(await SafeAsync(() => provider.GetQuotesAsync(watch.Where(w => w.Market != "cripto"), ct)));
        quotes.AddRange(watch.Where(w => w.Market == "cripto")
            .Select(w => cryptoQuotes.FirstOrDefault(q => q.Symbol.Equals(w.Symbol, StringComparison.OrdinalIgnoreCase))).OfType<Quote>());

        var live = new Live(DateTimeOffset.Now, positions, mep, quotes);
        await SaveSnapshotAsync(live, ct);
        return live;
    }

    static async Task<IReadOnlyList<Quote>> SafeAsync(Func<Task<IReadOnlyList<Quote>>> f)
    {
        try { return await f(); }
        catch (HttpRequestException) { return []; }
    }

    /// <summary>Reemplaza la foto del día y deja el total en pesos como valuación de Familia (la usa Ahorro e inversión).</summary>
    async Task SaveSnapshotAsync(Live live, CancellationToken ct)
    {
        if (live.Positions.Count == 0) return;
        var today = DateOnly.FromDateTime(live.AsOf.DateTime);
        await db.InvestmentSnapshots.Where(s => s.Date == today).ExecuteDeleteAsync(ct);
        db.InvestmentSnapshots.AddRange(live.Positions.Select(p => new InvestmentSnapshot
        {
            Date = today, Source = p.Source, Symbol = p.Symbol, Description = p.Description, AssetType = p.Type, Currency = p.Currency,
            Market = p.Market, Quantity = p.Quantity, LastPrice = p.LastPrice, AvgCost = p.AvgCost, DailyChangePct = p.DailyChangePct,
            GainPct = p.GainPct, GainAmount = p.GainAmount, GainIsComputed = p.GainIsComputed, MarketValue = p.MarketValue,
            MarketValueArs = Money.Cents(InvestmentMath.ToArs(p.MarketValue, p.Currency, live.Mep)),
            MarketValueUsd = Money.Cents(InvestmentMath.ToUsd(p.MarketValue, p.Currency, live.Mep)),
            Mep = live.Mep, TakenAt = live.AsOf,
        }).GroupBy(s => (s.Source, s.Symbol)).Select(g => g.First()));

        var total = Money.Cents(live.Positions.Sum(p => InvestmentMath.ToArs(p.MarketValue, p.Currency, live.Mep)));
        var valuation = await db.InvestmentValuations.FirstOrDefaultAsync(v => v.Scope == Scope.Familia && v.Date == today && v.Source == provider.Name, ct);
        if (valuation is null) db.InvestmentValuations.Add(new InvestmentValuation { Scope = Scope.Familia, Date = today, MarketValue = total, Source = provider.Name });
        else valuation.MarketValue = total;
        await db.SaveChangesAsync(ct);
    }

    async Task<Live> LastSnapshotAsync(CancellationToken ct)
    {
        var last = await db.InvestmentSnapshots.OrderByDescending(s => s.Date).Select(s => (DateOnly?)s.Date).FirstOrDefaultAsync(ct);
        if (last is null) return new Live(DateTimeOffset.Now, [], null, []);
        var rows = await db.InvestmentSnapshots.Where(s => s.Date == last).ToListAsync(ct);
        return new Live(rows.Max(r => r.TakenAt), rows.Select(ToPosition).ToList(), rows.Select(r => r.Mep).FirstOrDefault(m => m is not null), []);
    }

    static InvestmentPosition ToPosition(InvestmentSnapshot s) => new(
        s.Source, s.Symbol, s.Description, s.AssetType, s.Currency, s.Quantity, s.LastPrice, s.MarketValue,
        s.AvgCost, s.DailyChangePct, s.GainPct, s.GainAmount, s.GainIsComputed, s.Market);

    async Task<InvestmentDashboardDto> BuildAsync(Live live, bool stale, string? error, CancellationToken ct)
    {
        var mep = live.Mep;
        var positions = live.Positions;
        var totalArs = positions.Sum(p => InvestmentMath.ToArs(p.MarketValue, p.Currency, mep));
        decimal Pct(decimal ars) => totalArs > 0 ? Math.Round(ars / totalArs * 100, 2) : 0;

        List<SliceDto> Slices<TKey>(Func<InvestmentPosition, TKey> key, Func<TKey, string> label) where TKey : notnull =>
            positions.GroupBy(key).Select(g =>
            {
                var ars = g.Sum(p => InvestmentMath.ToArs(p.MarketValue, p.Currency, mep));
                var usd = g.Sum(p => InvestmentMath.ToUsd(p.MarketValue, p.Currency, mep));
                return new SliceDto(Camel(g.Key.ToString()!), label(g.Key), Math.Round(ars, 2), Math.Round(usd, 2), Pct(ars));
            }).OrderByDescending(s => s.Ars).ToList();

        var settings = await db.InvestmentSettings.FirstAsync(ct);
        var targets = await db.InvestmentTargets.Where(t => t.TargetPct > 0).ToDictionaryAsync(t => t.AssetType, t => t.TargetPct, ct);
        decimal? surplus = null;
        if (positions.Count > 0)
        {
            var cutoff = Endpoints.Today();
            var (calc, _) = await Endpoints.Calculator(db, await db.Categories.ToListAsync(ct), await db.Budgets.ToListAsync(ct),
                await db.Transactions.Include(t => t.Impacts).ToListAsync(ct), cutoff, Months.EndOf(cutoff));
            var floor = (await db.Settings.FirstAsync(ct)).CashFloor;
            surplus = Money.Pesos(calc.ProjectedAt(Scope.Familia, cutoff, Months.EndOf(cutoff)) - floor);
        }
        var signals = InvestmentSignals.Compute(positions, mep, targets,
            new SignalSettings(settings.MaxAssetPct, settings.MaxGroupPct, settings.TargetDeviationPts, Money.Pesos(settings.IdleCashArs), Money.Pesos(settings.IdleCashUsd)),
            surplus);

        return new InvestmentDashboardDto(
            positions.Count > 0 ? live.AsOf : null, stale, error, provider.IsConnected, mep,
            TotalsDto.From(InvestmentMath.Totals(positions, InvestmentCurrency.Ars, mep)),
            TotalsDto.From(InvestmentMath.Totals(positions, InvestmentCurrency.Usd, mep)),
            Slices(p => p.Type, InvestmentSignals.TypeName),
            Slices(p => p.Currency, c => c == InvestmentCurrency.Ars ? "Pesos" : "Dólares"),
            Slices(p => p.Source, s => s == "ripio" ? "Ripio" : "IOL"),
            positions.OrderByDescending(p => InvestmentMath.ToArs(p.MarketValue, p.Currency, mep)).Select(p =>
            {
                var ars = InvestmentMath.ToArs(p.MarketValue, p.Currency, mep);
                return new HoldingDto(p.Source, p.Symbol, p.Description, p.Type, p.Currency, p.Market, p.Quantity, p.LastPrice, p.AvgCost,
                    p.DailyChangePct, p.GainPct, p.GainAmount, p.GainIsComputed, p.MarketValue, Math.Round(ars, 2),
                    Math.Round(InvestmentMath.ToUsd(p.MarketValue, p.Currency, mep), 2), Pct(ars));
            }).ToList(),
            live.Quotes.Select(q => new QuoteDto(q.Symbol, q.Market, q.Currency, q.LastPrice, q.DailyChangePct, q.Description)).ToList(),
            signals, targets.Count > 0);
    }

    static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>"GGAL" → (bCBA, GGAL); "nyse:SPY" → (nYSE, SPY); "cripto:BTC" → (cripto, BTC).</summary>
    public static List<(string Market, string Symbol)> ParseWatchList(string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Split(':', 2, StringSplitOptions.TrimEntries))
            .Select(parts => parts.Length == 2 ? (IolMarket(parts[0]), parts[1].ToUpperInvariant()) : ("bCBA", parts[0].ToUpperInvariant()))
            .Where(w => w.Item2.Length > 0)
            .Distinct()
            .ToList();

    /// <summary>IOL escribe los mercados con la primera letra en minúscula (bCBA, nYSE, nASDAQ).</summary>
    public static string IolMarket(string market) => market.ToLowerInvariant() switch
    {
        "cripto" => "cripto",
        "" or "bcba" => "bCBA",
        var m => m[0] + m[1..].ToUpperInvariant(),
    };
}

public static class InvestmentEndpoints
{
    public static void MapInvestments(this WebApplication app)
    {
        var g = app.MapGroup("/investments");

        g.MapGet("/dashboard", (bool? refresh, InvestmentService service, CancellationToken ct) => service.DashboardAsync(refresh ?? false, ct));

        // Fuerza una actualización (igual que el botón Actualizar). Sin IOL ni tenencias manuales responde 503.
        g.MapPost("/sync", async (string? scope, InvestmentService service, IInvestmentProvider provider, FinanzasDbContext db, CancellationToken ct) =>
        {
            if (Endpoints.ScopeOrFamilia(scope) != Scope.Familia) return Results.NotFound("Las inversiones son de Familia.");
            if (!provider.IsConnected && !await db.ManualHoldings.AnyAsync(h => h.Active, ct))
                return Results.Problem("Invertir Online todavía no está conectado.", statusCode: StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(await service.DashboardAsync(true, ct));
        });

        g.MapGet("/history", async (DateOnly? from, DateOnly? to, FinanzasDbContext db, CancellationToken ct) =>
        {
            var rows = await db.InvestmentSnapshots
                .Where(s => (from == null || s.Date >= from) && (to == null || s.Date <= to))
                .Select(s => new { s.Date, s.MarketValueArs, s.MarketValueUsd })
                .ToListAsync(ct);
            return rows.GroupBy(r => r.Date).OrderBy(g => g.Key)
                .Select(g => new HistoryPointDto(g.Key, Money.Pesos(g.Sum(r => r.MarketValueArs)), Money.Pesos(g.Sum(r => r.MarketValueUsd))))
                .ToList();
        });

        g.MapGet("/assets/{symbol}/history", async (string symbol, string? market, int? months, IInvestmentProvider provider, ICryptoPrices crypto, CancellationToken ct) =>
        {
            var to = Endpoints.Today();
            var from = to.AddMonths(-Math.Clamp(months ?? 6, 1, 60));
            try
            {
                var points = market?.ToLowerInvariant() == "cripto"
                    ? await crypto.GetPriceHistoryAsync(symbol, to.DayNumber - from.DayNumber, ct)
                    : await provider.GetPriceHistoryAsync(InvestmentService.IolMarket(market ?? "bcba"), symbol, from, to, ct);
                return Results.Ok(points);
            }
            catch (Exception e) when (e is InvestmentProviderException or HttpRequestException)
            {
                return Results.Problem("No se pudo traer la serie de precios.", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        g.MapGet("/manual", async (FinanzasDbContext db) =>
            (await db.ManualHoldings.OrderBy(h => h.Symbol).ToListAsync()).Select(ManualHoldingDto.From));

        g.MapPost("/manual", async (SaveManualHoldingDto body, FinanzasDbContext db, IMemoryCache cache) =>
        {
            if (Invalid(body) is { } bad) return bad;
            var h = new ManualHolding { Symbol = body.Symbol.Trim().ToUpperInvariant(), Quantity = body.Quantity, CostUsd = body.CostUsd, Active = body.Active };
            db.ManualHoldings.Add(h);
            await db.SaveChangesAsync();
            cache.Remove("investments:live");
            return Results.Created($"/investments/manual/{h.Id}", ManualHoldingDto.From(h));
        });

        g.MapPut("/manual/{id:int}", async (int id, SaveManualHoldingDto body, FinanzasDbContext db, IMemoryCache cache) =>
        {
            if (Invalid(body) is { } bad) return bad;
            var h = await db.ManualHoldings.FindAsync(id);
            if (h is null) return Results.NotFound();
            h.Symbol = body.Symbol.Trim().ToUpperInvariant();
            h.Quantity = body.Quantity;
            h.CostUsd = body.CostUsd;
            h.Active = body.Active;
            await db.SaveChangesAsync();
            cache.Remove("investments:live");
            return Results.Ok(ManualHoldingDto.From(h));
        });

        g.MapDelete("/manual/{id:int}", async (int id, FinanzasDbContext db, IMemoryCache cache) =>
        {
            var h = await db.ManualHoldings.FindAsync(id);
            if (h is null) return Results.NotFound();
            db.ManualHoldings.Remove(h);
            await db.SaveChangesAsync();
            cache.Remove("investments:live");
            return Results.NoContent();
        });

        g.MapGet("/targets", async (FinanzasDbContext db) =>
            (await db.InvestmentTargets.OrderBy(t => t.AssetType).ToListAsync()).Select(t => new InvestmentTargetDto(t.AssetType, t.TargetPct)));

        // Reemplaza la cartera objetivo completa. Los porcentajes tienen que sumar 100 (o la lista ir vacía para no usarla).
        g.MapPut("/targets", async (List<InvestmentTargetDto> body, FinanzasDbContext db) =>
        {
            var items = body.Where(t => t.TargetPct > 0).ToList();
            if (items.Any(t => t.TargetPct > 100) || items.GroupBy(t => t.AssetType).Any(x => x.Count() > 1))
                return Results.BadRequest("Cada tipo de activo va una sola vez, con un porcentaje entre 0 y 100.");
            if (items.Count > 0 && items.Sum(t => t.TargetPct) != 100)
                return Results.BadRequest($"La cartera objetivo tiene que sumar 100% (hoy suma {items.Sum(t => t.TargetPct)}%).");
            db.InvestmentTargets.RemoveRange(db.InvestmentTargets);
            db.InvestmentTargets.AddRange(items.Select(t => new InvestmentTarget { AssetType = t.AssetType, TargetPct = t.TargetPct }));
            await db.SaveChangesAsync();
            return Results.Ok(items);
        });

        g.MapGet("/settings", async (FinanzasDbContext db) => InvestmentSettingsDto.From(await db.InvestmentSettings.FirstAsync()));

        g.MapPut("/settings", async (InvestmentSettingsDto body, FinanzasDbContext db, IMemoryCache cache) =>
        {
            if (body.MaxAssetPct is <= 0 or > 100 || body.MaxGroupPct is <= 0 or > 100 || body.TargetDeviationPts is < 0 or > 100 || body.IdleCashArs < 0 || body.IdleCashUsd < 0)
                return Results.BadRequest("Los porcentajes van entre 0 y 100 y los montos no pueden ser negativos.");
            var s = await db.InvestmentSettings.FirstAsync();
            s.MaxAssetPct = body.MaxAssetPct;
            s.MaxGroupPct = body.MaxGroupPct;
            s.TargetDeviationPts = body.TargetDeviationPts;
            s.IdleCashArs = Money.Cents(body.IdleCashArs);
            s.IdleCashUsd = Money.Cents(body.IdleCashUsd);
            s.WatchList = string.Join(",", body.WatchList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            await db.SaveChangesAsync();
            cache.Remove("investments:live");
            return Results.Ok(InvestmentSettingsDto.From(s));
        });

        static IResult? Invalid(SaveManualHoldingDto b) =>
            string.IsNullOrWhiteSpace(b.Symbol) || b.Quantity < 0 || b.CostUsd < 0
                ? Results.BadRequest("Cargá el activo (por ejemplo BTC) y una cantidad y costo que no sean negativos.") : null;
    }
}
