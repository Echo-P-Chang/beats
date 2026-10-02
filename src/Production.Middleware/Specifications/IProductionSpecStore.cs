using Beats.Production.Contracts.Specifications;

namespace Beats.Production.Middleware.Specifications;

public interface IProductionSpecStore
{
    Task<string> SaveAsync(
        ProductionSpec spec,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<ProductionSpec> LoadAsync(
        string specUri,
        CancellationToken cancellationToken = default);
}
