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
using System.Text;

namespace Beats.Agents.Animator;

public sealed class SceneImagesCreatedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
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

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var inputArtifact = incoming.Payload.Images.FirstOrDefault()?.Artifact;
        var manuscript = new StringBuilder();

        manuscript.AppendLine($"# Production {incoming.ProductionId}");
        manuscript.AppendLine();
        manuscript.AppendLine("## Animator");
        manuscript.AppendLine("Motion plan:");
        manuscript.AppendLine("The animator received image artifacts and creates timing language for each scene.");
        manuscript.AppendLine();
        manuscript.AppendLine("Scene animation placeholders:");

        foreach (var image in incoming.Payload.Images.OrderBy(image => image.Order))
        {
            manuscript.AppendLine($"- {image.SceneId}: Slow parallax push-in, gentle camera drift, soft transition, 8 seconds.");
            manuscript.AppendLine($"  Source image: {image.Artifact.Uri}");
        }

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript.ToString(),
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

        var publication = productionFlow.GetRequiredPublication<SceneAnimationsCreatedPayload>(
            AgentRoles.Animator,
            incoming.EventType);

        var outgoing = EventEnvelope<SceneAnimationsCreatedPayload>.Create(
            publication.EventType,
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
                inputArtifact?.Uri,
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
