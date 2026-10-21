namespace Finanzas.Domain;

public enum AssetType { Acciones, Cedears, Bonos, Ons, Fci, Cripto, Efectivo, Otros }

public enum InvestmentCurrency { Ars, Usd }

/// <summary>
/// Una tenencia tal como la informa su origen (IOL o la carga manual de Ripio). Precios y montos en la moneda del activo,
/// sin convertir. Los campos de rendimiento son los del broker y la app no los recalcula; solo en Ripio con costo cargado
/// los calcula la app y lo marca con <see cref="GainIsComputed"/>.
/// </summary>
public record InvestmentPosition(
    string Source, string Symbol, string Description, AssetType Type, InvestmentCurrency Currency,
    decimal Quantity, decimal LastPrice, decimal MarketValue,
    decimal? AvgCost = null, decimal? DailyChangePct = null, decimal? GainPct = null, decimal? GainAmount = null,
    bool GainIsComputed = false, string? Market = null);

/// <summary>Foto del portafolio de un proveedor. <see cref="Mep"/> es el dólar MEP del momento, si el proveedor lo sabe.</summary>
public record InvestmentPortfolio(DateTimeOffset AsOf, IReadOnlyList<InvestmentPosition> Positions, decimal? Mep = null);

/// <summary>Cotización de un activo (de la cartera o de la lista de seguimiento).</summary>
public record Quote(string Symbol, string Market, InvestmentCurrency Currency, decimal LastPrice, decimal? DailyChangePct, string? Description = null);

public record PricePoint(DateOnly Date, decimal Price);

/// <summary>
/// Punto de conexión con un broker. <c>SinProveedorDeInversiones</c> se usa cuando no hay credenciales; con ellas se
/// registra <c>IolInvestmentProvider</c>. Solo lectura: ningún proveedor compra, vende ni transfiere.
/// </summary>
public interface IInvestmentProvider
{
    /// <summary>Nombre estable del proveedor, se guarda como origen de cada valuación (ej. "invertirOnline").</summary>
    string Name { get; }
    bool IsConnected { get; }
    /// <summary>Portafolio del ámbito al momento, con el saldo disponible como tenencias de tipo Efectivo. Null si no hay cuenta para ese ámbito.</summary>
    Task<InvestmentPortfolio?> GetPortfolioAsync(Scope scope, CancellationToken ct = default);
    /// <summary>Cotizaciones de una lista de (mercado, símbolo). Los que no se encuentran se omiten.</summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<(string Market, string Symbol)> items, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string market, string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>Precios de cripto en USD desde una API pública, para las tenencias manuales de Ripio.</summary>
public interface ICryptoPrices
{
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(string symbol, int days, CancellationToken ct = default);
}

/// <summary>Conversión entre pesos y dólares con el MEP. Sin MEP, lo que está en la otra moneda vale 0 y se avisa aparte.</summary>
public static class InvestmentMath
{
    public static decimal ToArs(decimal amount, InvestmentCurrency currency, decimal? mep) =>
        currency == InvestmentCurrency.Ars ? amount : amount * (mep ?? 0);

    public static decimal ToUsd(decimal amount, InvestmentCurrency currency, decimal? mep) =>
        currency == InvestmentCurrency.Usd ? amount : mep is > 0 ? amount / mep.Value : 0;

    public static decimal In(decimal amount, InvestmentCurrency from, InvestmentCurrency to, decimal? mep) =>
        to == InvestmentCurrency.Ars ? ToArs(amount, from, mep) : ToUsd(amount, from, mep);

    /// <summary>Dólar MEP = precio del AL30 en pesos ÷ precio del AL30D en dólares (los dos por 100 nominales).</summary>
    public static decimal? Mep(decimal? al30Ars, decimal? al30dUsd) =>
        al30Ars is > 0 && al30dUsd is > 0 ? Math.Round(al30Ars.Value / al30dUsd.Value, 2) : null;

    /// <summary>
    /// Totales de la cartera en una moneda. Valor y ganancia son sumas de lo informado por el broker. El % total se toma
    /// sobre el costo implícito (valor − ganancia) de los activos que informan ganancia; la variación del día en $ sale
    /// de la variación % de cada activo.
    /// </summary>
    public static PortfolioTotals Totals(IEnumerable<InvestmentPosition> positions, InvestmentCurrency to, decimal? mep)
    {
        decimal value = 0, gain = 0, gainBase = 0, daily = 0, cash = 0;
        foreach (var p in positions)
        {
            var v = In(p.MarketValue, p.Currency, to, mep);
            value += v;
            if (p.Type == AssetType.Efectivo) { cash += v; continue; }
            if (p.GainAmount is { } g)
            {
                var gc = In(g, p.Currency, to, mep);
                gain += gc;
                gainBase += v - gc;
            }
            if (p.DailyChangePct is { } d && d != -100) daily += v - v / (1 + d / 100);
        }
        return new PortfolioTotals(value, gain, gainBase > 0 ? Math.Round(gain / gainBase * 100, 2) : null, daily,
            value - daily > 0 ? Math.Round(daily / (value - daily) * 100, 2) : null, cash);
    }
}

public record PortfolioTotals(decimal Value, decimal Gain, decimal? GainPct, decimal DailyChange, decimal? DailyChangePct, decimal Cash);

public record InvestmentSignal(string Kind, string Severity, string Title, string Detail);

public record SignalSettings(decimal MaxAssetPct, decimal MaxGroupPct, decimal TargetDeviationPts, decimal IdleCashArs, decimal IdleCashUsd);

/// <summary>
/// Señales informativas de la sección "Oportunidades y rotación" (spec Spec/inversiones-dashboard.md):
/// A concentración, B desvío contra la cartera objetivo, D plata quieta. Son análisis de los datos, no recomendaciones.
/// </summary>
public static class InvestmentSignals
{
    public static List<InvestmentSignal> Compute(
        IReadOnlyList<InvestmentPosition> positions, decimal? mep, IReadOnlyDictionary<AssetType, decimal> targets,
        SignalSettings s, decimal? projectedCashSurplus)
    {
        var signals = new List<InvestmentSignal>();
        var valued = positions.Select(p => (p, ars: InvestmentMath.ToArs(p.MarketValue, p.Currency, mep))).ToList();
        var total = valued.Sum(x => x.ars);

        if (total > 0)
        {
            // A. Concentración: por activo, por tipo y por moneda.
            foreach (var x in valued.Where(x => x.p.Type != AssetType.Efectivo).OrderByDescending(x => x.ars))
            {
                var pct = x.ars / total * 100;
                if (pct > s.MaxAssetPct)
                    signals.Add(new("concentracion", "atencion", $"{x.p.Symbol} es el {Pct(pct)} de tu cartera",
                        $"Supera el {Pct(s.MaxAssetPct)} que marcaste como máximo por activo. Vale {Ars(x.ars)}."));
            }
            foreach (var g in valued.GroupBy(x => x.p.Type).OrderByDescending(g => g.Sum(x => x.ars)))
            {
                var pct = g.Sum(x => x.ars) / total * 100;
                if (pct > s.MaxGroupPct)
                    signals.Add(new("concentracion", "atencion", $"{TypeName(g.Key)} son el {Pct(pct)} de tu cartera",
                        $"Supera el {Pct(s.MaxGroupPct)} que marcaste como máximo por tipo de activo."));
            }
            foreach (var g in valued.GroupBy(x => x.p.Currency))
            {
                var pct = g.Sum(x => x.ars) / total * 100;
                if (pct > s.MaxGroupPct)
                    signals.Add(new("concentracion", "info", $"El {Pct(pct)} de tu cartera está en {(g.Key == InvestmentCurrency.Ars ? "pesos" : "dólares")}",
                        $"Supera el {Pct(s.MaxGroupPct)} que marcaste como máximo por moneda."));
            }

            // B. Desvío contra la cartera objetivo, con la plata que habría que mover para volver a ella.
            if (targets.Count > 0)
            {
                var byType = valued.GroupBy(x => x.p.Type).ToDictionary(g => g.Key, g => g.Sum(x => x.ars));
                foreach (var type in targets.Keys.Union(byType.Keys).OrderBy(t => t))
                {
                    var actual = byType.GetValueOrDefault(type) / total * 100;
                    var target = targets.GetValueOrDefault(type);
                    var dev = actual - target;
                    if (Math.Abs(dev) <= s.TargetDeviationPts) continue;
                    var amount = Math.Abs(dev) / 100 * total;
                    signals.Add(new("objetivo", "atencion",
                        $"{TypeName(type)}: {Pct(actual)} (objetivo {Pct(target)})",
                        dev > 0 ? $"{Ars(amount)} por encima del objetivo." : $"Faltan {Ars(amount)} para llegar al objetivo."));
                }
            }
        }

        // D. Plata quieta: saldo disponible en el broker y excedente de la caja proyectada sobre el piso de caja.
        foreach (var cash in positions.Where(p => p.Type == AssetType.Efectivo && p.MarketValue > 0))
        {
            var min = cash.Currency == InvestmentCurrency.Ars ? s.IdleCashArs : s.IdleCashUsd;
            if (cash.MarketValue >= min)
                signals.Add(new("plataQuieta", "info",
                    $"Tenés {(cash.Currency == InvestmentCurrency.Ars ? Ars(cash.MarketValue) : Usd(cash.MarketValue))} sin invertir en {SourceName(cash.Source)}",
                    "Es saldo disponible en la cuenta, sin asignar a ningún activo."));
        }
        if (projectedCashSurplus is > 0)
            signals.Add(new("plataQuieta", "info", $"La caja proyectada a fin de mes supera tu piso de caja por {Ars(projectedCashSurplus.Value)}",
                "Sale de la proyección de caja Familia de la app y del piso de caja de Configuración."));

        return signals;
    }

    public static string TypeName(AssetType t) => t switch
    {
        AssetType.Acciones => "Acciones",
        AssetType.Cedears => "CEDEARs",
        AssetType.Bonos => "Bonos",
        AssetType.Ons => "ONs",
        AssetType.Fci => "FCI",
        AssetType.Cripto => "Cripto",
        AssetType.Efectivo => "Efectivo",
        _ => "Otros",
    };

    static string SourceName(string source) => source == "ripio" ? "Ripio" : "IOL";

    static readonly System.Globalization.CultureInfo Es = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
    static string Pct(decimal v) => $"{Math.Round(v, 1).ToString("0.#", Es)}%";
    static string Ars(decimal v) => $"${Math.Round(v).ToString("#,0", Es)}";
    static string Usd(decimal v) => $"US${Math.Round(v).ToString("#,0", Es)}";
}
