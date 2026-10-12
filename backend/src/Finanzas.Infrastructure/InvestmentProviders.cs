using Finanzas.Domain;

namespace Finanzas.Infrastructure;

/// <summary>Proveedor por defecto mientras Invertir Online no esté conectado: no informa valor de mercado.</summary>
public class SinProveedorDeInversiones : IInvestmentProvider
{
    public string Name => "invertirOnline";
    public bool IsConnected => false;
    public Task<InvestmentPortfolio?> GetPortfolioAsync(Scope scope, CancellationToken ct = default) => Task.FromResult<InvestmentPortfolio?>(null);
}
