namespace Beats.Production.Middleware.Persistence;

public sealed record ProductionEventRecord(
    Guid EventId,
    Guid ProductionId,
    string EventType,
    Guid CorrelationId,
    Guid? CausationId,
    string Producer,
    string SchemaVersion,
    DateTimeOffset CreatedAt);
