namespace Finanzas.Domain;

/// <summary>Una tenencia informada por el broker (por ejemplo, un bono o un FCI en Invertir Online).</summary>
public record InvestmentPosition(string Symbol, string Description, decimal Quantity, long MarketValue);

public record InvestmentPortfolio(DateOnly Date, IReadOnlyList<InvestmentPosition> Positions)
{
    public long MarketValue => Positions.Sum(p => p.MarketValue);
}

/// <summary>
/// Punto de conexión con un broker para traer el valor de mercado de las inversiones. Hoy no hay ninguno conectado
/// (<c>SinProveedorDeInversiones</c>); para Invertir Online se implementa esta interfaz contra su API (portafolio)
/// y se registra en Program.cs. Ver docs/arquitectura.md, sección "Inversiones e Invertir Online".
/// </summary>
public interface IInvestmentProvider
{
    /// <summary>Nombre estable del proveedor, se guarda como origen de cada valuación (ej. "invertirOnline").</summary>
    string Name { get; }
    bool IsConnected { get; }
    /// <summary>Portafolio del ámbito al día de hoy. Null si el proveedor no tiene cuenta para ese ámbito.</summary>
    Task<InvestmentPortfolio?> GetPortfolioAsync(Scope scope, CancellationToken ct = default);
}
