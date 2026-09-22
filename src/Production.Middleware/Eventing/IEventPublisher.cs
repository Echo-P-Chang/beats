using Beats.Production.Contracts.Events;

namespace Beats.Production.Middleware.Eventing;

public interface IEventPublisher
{
    Task PublishAsync<TPayload>(
        EventEnvelope<TPayload> message,
        CancellationToken cancellationToken = default);
}
