using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;

namespace Beats.Production.Middleware.Specifications;

public sealed class ProductionSpecValidator(IProductionSpecStore specStore) : IProductionSpecValidator
{
    public async Task<ProductionSpecValidationResult> ValidateAsync(
        EventEnvelope<CommonPayload> incoming,
        string agentRole,
        CancellationToken cancellationToken = default)
    {
        if (!incoming.Payload.Attributes.TryGetValue("specUri", out var specUri) ||
            string.IsNullOrWhiteSpace(specUri))
        {
            throw new InvalidOperationException(
                $"Event '{incoming.EventType}' is missing required attribute 'specUri'.");
        }

        var spec = await specStore.LoadAsync(specUri, cancellationToken);
        var stage = spec.Stages.FirstOrDefault(candidate =>
            candidate.AgentRole.Equals(agentRole, StringComparison.OrdinalIgnoreCase) &&
            candidate.TriggerEventType.Equals(incoming.EventType, StringComparison.OrdinalIgnoreCase));

        if (stage is null)
        {
            throw new InvalidOperationException(
                $"Production spec does not define a stage for agent '{agentRole}' triggered by '{incoming.EventType}'.");
        }

        var errors = new List<string>();

        foreach (var requiredAttribute in stage.RequiredAttributes)
        {
            if (!incoming.Payload.Attributes.TryGetValue(requiredAttribute, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"Missing required attribute '{requiredAttribute}'.");
            }
        }

        foreach (var requiredArtifactAttribute in stage.RequiredArtifactAttributes)
        {
            if (!incoming.Payload.Attributes.TryGetValue(requiredArtifactAttribute, out var artifactUri) ||
                string.IsNullOrWhiteSpace(artifactUri))
            {
                errors.Add($"Missing required artifact attribute '{requiredArtifactAttribute}'.");
                continue;
            }

            if (incoming.Payload.Artifacts.Count > 0 &&
                !incoming.Payload.Artifacts.Any(artifact =>
                    artifact.Uri.Equals(artifactUri, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"Artifact attribute '{requiredArtifactAttribute}' points to '{artifactUri}', but that artifact is not included in the event artifact list.");
            }
        }

        foreach (var requiredMediaType in stage.RequiredArtifactMediaTypes)
        {
            if (!incoming.Payload.Artifacts.Any(artifact =>
                IsMediaTypeMatch(artifact.MediaType, requiredMediaType)))
            {
                errors.Add($"Missing required artifact media type '{requiredMediaType}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(stage.PublishEventType))
        {
            errors.Add($"Stage '{agentRole}' does not define a publish event type.");
        }

        return new ProductionSpecValidationResult(
            errors.Count == 0,
            spec,
            stage,
            errors);
    }

    private static bool IsMediaTypeMatch(string mediaType, string requirement)
    {
        if (requirement.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = requirement[..^1];
            return mediaType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return mediaType.Equals(requirement, StringComparison.OrdinalIgnoreCase);
    }
}
