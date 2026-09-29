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
    ILogger<SceneImagesCreatedConsumer> logger) : IConsumer<EventEnvelope<CommonPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<CommonPayload>> context)
    {
        var incoming = context.Message;

        if (!productionFlow.IsSubscribedTo(AgentRoles.Animator, incoming.EventType))
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var imageArtifacts = incoming.Payload.Artifacts
            .Where(artifact => artifact.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, ImageCount={ImageCount}",
            AgentRoles.Animator,
            incoming.EventType,
            incoming.ProductionId,
            imageArtifacts.Length);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var inputArtifact = imageArtifacts.FirstOrDefault();
        var manuscript = new StringBuilder();

        manuscript.AppendLine($"# Production {incoming.ProductionId}");
        manuscript.AppendLine();
        manuscript.AppendLine("## Animator");
        manuscript.AppendLine("Motion plan:");
        manuscript.AppendLine("The animator received image artifacts and creates timing language for each scene.");
        manuscript.AppendLine();
        manuscript.AppendLine("Scene animation placeholders:");

        for (var index = 0; index < imageArtifacts.Length; index++)
        {
            var image = imageArtifacts[index];
            manuscript.AppendLine($"- scene-{index + 1:000}: Slow parallax push-in, gentle camera drift, soft transition, 8 seconds.");
            manuscript.AppendLine($"  Source image: {image.Uri}");
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

        var publication = productionFlow.GetRequiredPublication<CommonPayload>(
            AgentRoles.Animator,
            incoming.EventType);

        var payload = CommonPayload.Create(
            publication.EventType,
            AgentRoles.Animator,
            attributes: new Dictionary<string, string>(incoming.Payload.Attributes)
            {
                ["animationPlanUri"] = artifact.Uri,
                ["animationCount"] = "1"
            },
            artifacts: [artifact]);

        var outgoing = EventEnvelope<CommonPayload>.Create(
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
