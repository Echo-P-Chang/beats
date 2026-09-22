using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using MassTransit;

namespace Beats.Agents.Illustrator;

public sealed class StoryCreatedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionRepository productionRepository,
    ILogger<StoryCreatedConsumer> logger) : IConsumer<EventEnvelope<StoryCreatedPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<StoryCreatedPayload>> context)
    {
        var incoming = context.Message;
        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, Scenes={ScenesArtifact}",
            AgentRoles.Illustrator,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Scenes.Uri);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var manuscript = await ArtifactText.ReadAsync(
            artifactStore,
            incoming.Payload.Story.Uri,
            context.CancellationToken);

        manuscript += $"""


            ## Illustrator
            Visual direction:
            The illustrator keeps the artifact as text for now, but adds a consistent visual language: watercolor storybook, soft contrast, warm key light, recurring blue-gold accents, and clear scene notes for each major beat.

            Scene image placeholders:
            - scene-001: Establishing shot with the protagonist entering the story world.
            - scene-002: Emotional midpoint with stronger color contrast.
            - scene-003: Closing image with visual resolution.
            """;

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript,
            $"productions/{incoming.ProductionId}/02-illustrator/manuscript.txt",
            context.CancellationToken);

        var artifact = new ArtifactReference(
            artifactUri,
            "text/plain",
            "Manuscript enriched with visual direction by the illustrator agent.");

        var payload = new SceneImagesCreatedPayload([
            new SceneArtifact(
                "scene-001",
                artifact,
                1)
        ]);

        var outgoing = EventEnvelope<SceneImagesCreatedPayload>.Create(
            EventTypes.SceneImagesCreated,
            incoming.ProductionId,
            AgentRoles.Illustrator,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Illustrator,
            artifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Illustrator,
                incoming.EventId,
                outgoing.EventId,
                incoming.Payload.Story.Uri,
                artifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                "Added visual direction to the evolving text artifact."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.ImagesCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }
}
