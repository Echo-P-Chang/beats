using Beats.Production.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Beats.Production.Middleware.Eventing;

public sealed class MassTransitEventPublisher(
    IPublishEndpoint publishEndpoint,
    ILogger<MassTransitEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TPayload>(
        EventEnvelope<TPayload> message,
        CancellationToken cancellationToken = default)
    {
        await publishEndpoint.Publish(message, cancellationToken);

        logger.LogInformation(
            "Published {EventType} for production {ProductionId}. EventId={EventId}, CorrelationId={CorrelationId}",
            message.EventType,
            message.ProductionId,
            message.EventId,
            message.CorrelationId);
    }
}
