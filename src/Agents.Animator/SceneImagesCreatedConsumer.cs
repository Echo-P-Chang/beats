using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using MassTransit;

namespace Beats.Agents.Animator;

public sealed class SceneImagesCreatedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionRepository productionRepository,
    ILogger<SceneImagesCreatedConsumer> logger) : IConsumer<EventEnvelope<SceneImagesCreatedPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<SceneImagesCreatedPayload>> context)
    {
        var incoming = context.Message;
        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, ImageCount={ImageCount}",
            AgentRoles.Animator,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Images.Count);

        var inputArtifact = incoming.Payload.Images[0].Artifact;
        var manuscript = await ArtifactText.ReadAsync(
            artifactStore,
            inputArtifact.Uri,
            context.CancellationToken);

        manuscript += $"""


            ## Animator
            Motion plan:
            The animator converts the visual notes into timing language. Each scene receives camera movement, transition rhythm, and animation intent while preserving the same text artifact.

            Animation placeholders:
            - scene-001: Slow push-in, light page-turn transition, 8 seconds.
            - scene-002: Gentle parallax, character focus, 10 seconds.
            - scene-003: Hold on final composition, soft fade, 7 seconds.
            """;

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript,
            $"productions/{incoming.ProductionId}/03-animator/manuscript.txt",
            context.CancellationToken);

        var artifact = new ArtifactReference(
            artifactUri,
            "text/plain",
            "Manuscript enriched with motion direction by the animator agent.");

        var payload = new SceneAnimationsCreatedPayload([
            new SceneArtifact(
                "scene-001",
                artifact,
                1)
        ]);

        var outgoing = EventEnvelope<SceneAnimationsCreatedPayload>.Create(
            EventTypes.SceneAnimationsCreated,
            incoming.ProductionId,
            AgentRoles.Animator,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Animator,
            artifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Animator,
                incoming.EventId,
                outgoing.EventId,
                inputArtifact.Uri,
                artifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                "Added motion direction to the evolving text artifact."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.AnimationsCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }
}
