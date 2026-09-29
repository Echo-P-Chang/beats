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

namespace Beats.Agents.Editor;

public sealed class SceneAnimationsCreatedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
    IProductionRepository productionRepository,
    ILogger<SceneAnimationsCreatedConsumer> logger) : IConsumer<EventEnvelope<CommonPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<CommonPayload>> context)
    {
        var incoming = context.Message;

        if (!productionFlow.IsSubscribedTo(AgentRoles.Editor, incoming.EventType))
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var inputArtifact = incoming.Payload.Artifacts.First();

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, AnimationCount={AnimationCount}",
            AgentRoles.Editor,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Attributes.TryGetValue("animationCount", out var count) ? count : incoming.Payload.Artifacts.Count);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var manuscript = await ArtifactText.ReadAsync(
            artifactStore,
            inputArtifact.Uri,
            context.CancellationToken);

        manuscript += $"""


            ## Editor
            Edit plan:
            The editor adds final sequence structure, voiceover pacing, and audio notes. The artifact now reads like a compact production bible rather than only a story draft.

            Voiceover placeholder:
            A calm narrator reads the story with measured pauses, matching the three scene beats and leaving space for visual transitions.

            Final cut placeholder:
            Assemble scenes in order, normalize audio, apply gentle crossfades, and export one complete video package.
            """;

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript,
            $"productions/{incoming.ProductionId}/04-editor/manuscript.txt",
            context.CancellationToken);

        var artifact = new ArtifactReference(
            artifactUri,
            "text/plain",
            "Manuscript enriched with edit and voiceover direction by the editor agent.");

        var publication = productionFlow.GetRequiredPublication<CommonPayload>(
            AgentRoles.Editor,
            incoming.EventType);

        var payload = CommonPayload.Create(
            publication.EventType,
            AgentRoles.Editor,
            attributes: new Dictionary<string, string>(incoming.Payload.Attributes)
            {
                ["videoUri"] = artifact.Uri,
                ["voiceOverUri"] = artifact.Uri
            },
            artifacts: [artifact]);

        var outgoing = EventEnvelope<CommonPayload>.Create(
            publication.EventType,
            incoming.ProductionId,
            AgentRoles.Editor,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Editor,
            artifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Editor,
                incoming.EventId,
                outgoing.EventId,
                inputArtifact.Uri,
                artifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                "Added edit and voiceover direction to the evolving text artifact."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.FinalVideoCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }
}
