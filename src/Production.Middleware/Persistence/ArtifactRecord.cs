namespace Beats.Production.Middleware.Persistence;

public sealed record ArtifactRecord(
    Guid ArtifactId,
    Guid ProductionId,
    string AgentRole,
    string ArtifactUri,
    string MediaType,
    string? Description,
    DateTimeOffset CreatedAt);
