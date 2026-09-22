using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using MassTransit;

namespace Beats.Agents.Reviewer;

public sealed class FinalVideoCreatedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionRepository productionRepository,
    ILogger<FinalVideoCreatedConsumer> logger) : IConsumer<EventEnvelope<FinalVideoCreatedPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<FinalVideoCreatedPayload>> context)
    {
        var incoming = context.Message;
        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, Video={VideoArtifact}",
            AgentRoles.Reviewer,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Video.Uri);

        var manuscript = await ArtifactText.ReadAsync(
            artifactStore,
            incoming.Payload.Video.Uri,
            context.CancellationToken);

        manuscript += $"""


            ## Reviewer
            Quality review:
            Result: Passed.

            Checks:
            - Story draft exists.
            - Visual direction exists.
            - Motion direction exists.
            - Edit and voiceover direction exists.
            - Artifact chain remained readable as one evolving text file.

            Reviewer note:
            This is a dummy review. Later this step can inspect real media outputs and publish ReviewFailed with targeted revision requests.
            """;

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript,
            $"productions/{incoming.ProductionId}/05-reviewer/manuscript.txt",
            context.CancellationToken);

        var artifact = new ArtifactReference(
            artifactUri,
            "text/plain",
            "Final reviewed manuscript enriched by the reviewer agent.");

        var payload = new ReviewCompletedPayload(
            true,
            artifact,
            ["Stub review passed. Full quality checks will be implemented later."]);

        var outgoing = EventEnvelope<ReviewCompletedPayload>.Create(
            EventTypes.ReviewPassed,
            incoming.ProductionId,
            AgentRoles.Reviewer,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Reviewer,
            artifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Reviewer,
                incoming.EventId,
                outgoing.EventId,
                incoming.Payload.Video.Uri,
                artifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                "Added final review to the evolving text artifact."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.ReviewPassed.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }
}
