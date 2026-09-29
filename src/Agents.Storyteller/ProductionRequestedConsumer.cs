using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Flows;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using MassTransit;

namespace Beats.Agents.Storyteller;

public sealed class ProductionRequestedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
    IProductionRepository productionRepository,
    ILogger<ProductionRequestedConsumer> logger) : IConsumer<EventEnvelope<CommonPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<CommonPayload>> context)
    {
        var incoming = context.Message;

        if (!productionFlow.IsSubscribedTo(AgentRoles.Storyteller, incoming.EventType))
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var prompt = Attribute(incoming, "prompt");
        var style = Attribute(incoming, "style");
        var targetWordCount = Attribute(incoming, "targetWordCount");
        var durationSeconds = Attribute(incoming, "durationSeconds");

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, Prompt={Prompt}",
            AgentRoles.Storyteller,
            incoming.EventType,
            incoming.ProductionId,
            prompt);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var manuscript = $"""
            # Production {incoming.ProductionId}

            ## Storyteller
            Prompt: {prompt}
            Style: {Display(style)}
            Target word count: {Display(targetWordCount)}
            Target duration seconds: {Display(durationSeconds)}

            Story draft:
            A quiet opening appears here. The storyteller sketches the characters, the world, the central tension, and the emotional arc. Later agents will enrich this same artifact instead of replacing it.
            """;

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript,
            $"productions/{incoming.ProductionId}/01-storyteller/manuscript.txt",
            context.CancellationToken);

        var manuscriptArtifact = new ArtifactReference(
            artifactUri,
            "text/plain",
            "Manuscript initialized by the storyteller agent.");

        var publication = productionFlow.GetRequiredPublication<CommonPayload>(
            AgentRoles.Storyteller,
            incoming.EventType);

        var payload = CommonPayload.Create(
            publication.EventType,
            AgentRoles.Storyteller,
            attributes: new Dictionary<string, string>(incoming.Payload.Attributes)
            {
                ["storyUri"] = manuscriptArtifact.Uri,
                ["scenesUri"] = manuscriptArtifact.Uri
            },
            artifacts: [manuscriptArtifact]);

        var outgoing = EventEnvelope<CommonPayload>.Create(
            publication.EventType,
            incoming.ProductionId,
            AgentRoles.Storyteller,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Storyteller,
            manuscriptArtifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Storyteller,
                incoming.EventId,
                outgoing.EventId,
                null,
                manuscriptArtifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                "Initialized the evolving text artifact."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.StoryCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }

    private static string Attribute(EventEnvelope<CommonPayload> incoming, string key)
    {
        return incoming.Payload.Attributes.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "not specified" : value;
    }
}
