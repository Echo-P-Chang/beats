namespace Beats.Production.Middleware.Persistence;

public sealed record AgentRunRecord(
    Guid AgentRunId,
    Guid ProductionId,
    string AgentRole,
    Guid ConsumedEventId,
    Guid PublishedEventId,
    string? InputArtifactUri,
    string? OutputArtifactUri,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? Notes);
