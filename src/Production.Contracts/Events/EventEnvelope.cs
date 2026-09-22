namespace Beats.Production.Contracts.Events;

public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    Guid ProductionId,
    Guid CorrelationId,
    Guid? CausationId,
    string Producer,
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    TPayload Payload)
{
    public static EventEnvelope<TPayload> Create(
        string eventType,
        Guid productionId,
        string producer,
        TPayload payload,
        Guid? correlationId = null,
        Guid? causationId = null,
        string schemaVersion = "1.0")
    {
        return new EventEnvelope<TPayload>(
            Guid.NewGuid(),
            eventType,
            productionId,
            correlationId ?? Guid.NewGuid(),
            causationId,
            producer,
            schemaVersion,
            DateTimeOffset.UtcNow,
            payload);
    }
}
