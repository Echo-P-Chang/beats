using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Flows;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Contracts.Storyboarding;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using Beats.Production.Middleware.Specifications;
using MassTransit;

namespace Beats.Agents.Animator;

public sealed class SceneImagesCreatedConsumer(
    IArtifactStore artifactStore,
    IAnimatorAdapter animatorAdapter,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
    IProductionRepository productionRepository,
    IProductionSpecValidator productionSpecValidator,
    ILogger<SceneImagesCreatedConsumer> logger) : IConsumer<EventEnvelope<CommonPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<CommonPayload>> context)
    {
        var incoming = context.Message;

        if (!productionFlow.IsSubscribedTo(AgentRoles.Animator, incoming.EventType))
        {
            return;
        }

        var specValidation = await productionSpecValidator.ValidateAsync(
            incoming,
            AgentRoles.Animator,
            context.CancellationToken);
        EnsureValidSpec(specValidation);

        var startedAt = DateTimeOffset.UtcNow;
        var artifacts = incoming.Payload.Artifacts.ToArray();
        var imageArtifacts = incoming.Payload.Artifacts
            .Where(artifact => artifact.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var storyArtifact = GetOptionalArtifact(incoming, "storyUri");
        var sceneBreakdownArtifact = GetOptionalArtifact(incoming, "sceneBreakdownUri", "scenesUri");
        var animationPlanArtifact = GetOptionalArtifact(incoming, "animationPlanUri");

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, ImageCount={ImageCount}",
            AgentRoles.Animator,
            incoming.EventType,
            incoming.ProductionId,
            imageArtifacts.Length);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var inputArtifact = imageArtifacts.FirstOrDefault();
        var storyText = storyArtifact is null
            ? null
            : await ArtifactText.ReadAsync(
                artifactStore,
                storyArtifact.Uri,
                context.CancellationToken);
        var sceneBreakdown = sceneBreakdownArtifact is null
            ? null
            : await ArtifactJson.ReadAsync<SceneBreakdownDocument>(
                artifactStore,
                sceneBreakdownArtifact.Uri,
                context.CancellationToken);
        var sourceAnimationPlan = animationPlanArtifact is null
            ? null
            : await ArtifactJson.ReadAsync<AnimationPlanDocument>(
                artifactStore,
                animationPlanArtifact.Uri,
                context.CancellationToken);
        var animationPlan = await animatorAdapter.CreateAnimationPlanAsync(
            new AnimationPlanRequest(
                incoming.ProductionId,
                incoming.EventType,
                new Dictionary<string, string>(incoming.Payload.Attributes),
                artifacts,
                imageArtifacts,
                storyText,
                sceneBreakdown,
                sourceAnimationPlan),
            context.CancellationToken);

        var generatedArtifacts = new List<ArtifactReference>();

        foreach (var generatedArtifact in animationPlan.GeneratedArtifacts)
        {
            await using var generatedContent = new MemoryStream(generatedArtifact.Content);
            var generatedArtifactUri = await artifactStore.SaveAsync(
                generatedContent,
                $"productions/{incoming.ProductionId}/03-animator/{generatedArtifact.FileName}",
                generatedArtifact.MediaType,
                context.CancellationToken);

            generatedArtifacts.Add(new ArtifactReference(
                generatedArtifactUri,
                generatedArtifact.MediaType,
                generatedArtifact.Description));
        }

        var reportUri = await ArtifactText.SaveAsync(
            artifactStore,
            animationPlan.Content,
            $"productions/{incoming.ProductionId}/03-animator/animation-report.txt",
            context.CancellationToken);

        var reportArtifact = new ArtifactReference(
            reportUri,
            "text/plain",
            "Animator execution report describing source plans, generated animation artifacts, and provider output.");

        var publication = productionFlow.GetRequiredPublication<CommonPayload>(
            AgentRoles.Animator,
            incoming.EventType);

        var passthroughArtifacts = new[] { storyArtifact, sceneBreakdownArtifact, animationPlanArtifact }
            .Where(artifact => artifact is not null)
            .Select(artifact => artifact!)
            .ToArray();
        var outputArtifacts = generatedArtifacts
            .Concat([reportArtifact])
            .Concat(passthroughArtifacts)
            .ToArray();

        var firstVideoArtifact = generatedArtifacts.FirstOrDefault(
            generatedArtifact => generatedArtifact.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));
        var animationVideoUris = generatedArtifacts
            .Where(generatedArtifact => generatedArtifact.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .Select(generatedArtifact => generatedArtifact.Uri)
            .ToArray();

        var payload = CommonPayload.Create(
            publication.EventType,
            AgentRoles.Animator,
            attributes: new Dictionary<string, string>(incoming.Payload.Attributes)
            {
                ["animationReportUri"] = reportArtifact.Uri,
                ["animationCount"] = animationPlan.AnimationCount.ToString(),
                ["animationVideoUri"] = firstVideoArtifact?.Uri ?? string.Empty,
                ["animationVideoUris"] = string.Join("|", animationVideoUris)
            },
            artifacts: outputArtifacts);

        var outgoing = EventEnvelope<CommonPayload>.Create(
            publication.EventType,
            incoming.ProductionId,
            AgentRoles.Animator,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        foreach (var generatedArtifact in generatedArtifacts)
        {
            await productionRepository.RecordArtifactAsync(
                incoming.ProductionId,
                AgentRoles.Animator,
                generatedArtifact,
                context.CancellationToken);
        }

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Animator,
            reportArtifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Animator,
                incoming.EventId,
                outgoing.EventId,
                inputArtifact?.Uri,
                firstVideoArtifact?.Uri ?? reportArtifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                animationPlan.Summary),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.AnimationsCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }

    private static void EnsureValidSpec(ProductionSpecValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Production spec preflight failed for {validation.Stage.AgentRole}: {string.Join("; ", validation.Errors)}");
        }
    }

    private static ArtifactReference? GetOptionalArtifact(
        EventEnvelope<CommonPayload> incoming,
        params string[] outputKeys)
    {
        foreach (var outputKey in outputKeys)
        {
            if (!incoming.Payload.Attributes.TryGetValue(outputKey, out var uri) ||
                string.IsNullOrWhiteSpace(uri))
            {
                continue;
            }

            return incoming.Payload.Artifacts.FirstOrDefault(artifact =>
                artifact.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)) ??
                new ArtifactReference(uri, "application/json");
        }

        return null;
    }
}
