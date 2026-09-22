using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Media;

namespace Beats.Production.Middleware.Persistence;

public interface IProductionRepository
{
    Task CreateProductionAsync(
        Guid productionId,
        string prompt,
        string? style,
        int targetWordCount,
        int? durationSeconds,
        string status,
        CancellationToken cancellationToken = default);

    Task<ProductionRecord?> GetProductionAsync(
        Guid productionId,
        CancellationToken cancellationToken = default);

    Task UpdateProductionStatusAsync(
        Guid productionId,
        string status,
        CancellationToken cancellationToken = default);

    Task RecordEventAsync<TPayload>(
        EventEnvelope<TPayload> message,
        CancellationToken cancellationToken = default);

    Task RecordArtifactAsync(
        Guid productionId,
        string agentRole,
        ArtifactReference artifact,
        CancellationToken cancellationToken = default);

    Task RecordAgentRunAsync(
        AgentRunRecord record,
        CancellationToken cancellationToken = default);
}
