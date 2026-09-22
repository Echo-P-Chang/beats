using Beats.Production.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Beats.Production.Middleware.Eventing;

public sealed class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync<TPayload>(
        EventEnvelope<TPayload> message,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Published {EventType} for production {ProductionId}. EventId={EventId}, CorrelationId={CorrelationId}",
            message.EventType,
            message.ProductionId,
            message.EventId,
            message.CorrelationId);

        return Task.CompletedTask;
    }
}
