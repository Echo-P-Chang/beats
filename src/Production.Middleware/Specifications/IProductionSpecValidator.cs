using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;

namespace Beats.Production.Middleware.Specifications;

public interface IProductionSpecValidator
{
    Task<ProductionSpecValidationResult> ValidateAsync(
        EventEnvelope<CommonPayload> incoming,
        string agentRole,
        CancellationToken cancellationToken = default);
}
