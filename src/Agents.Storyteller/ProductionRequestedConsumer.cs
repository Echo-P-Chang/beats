using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
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
    IProductionRepository productionRepository,
    ILogger<ProductionRequestedConsumer> logger) : IConsumer<EventEnvelope<ProductionRequestedPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<ProductionRequestedPayload>> context)
    {
        var incoming = context.Message;
        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, Prompt={Prompt}",
            AgentRoles.Storyteller,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Prompt);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var manuscript = $"""
            # Production {incoming.ProductionId}

            ## Storyteller
            Prompt: {incoming.Payload.Prompt}
            Style: {incoming.Payload.Style ?? "not specified"}
            Target word count: {incoming.Payload.TargetWordCount}
            Target duration seconds: {incoming.Payload.DurationSeconds?.ToString() ?? "not specified"}

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

        var payload = new StoryCreatedPayload(
            manuscriptArtifact,
            manuscriptArtifact);

        var outgoing = EventEnvelope<StoryCreatedPayload>.Create(
            EventTypes.StoryCreated,
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
}
