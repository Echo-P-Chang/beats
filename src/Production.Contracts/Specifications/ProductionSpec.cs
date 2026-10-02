namespace Beats.Production.Contracts.Specifications;

public sealed record ProductionSpec(
    Guid ProductionId,
    int SpecVersion,
    string Prompt,
    string? Style,
    int TargetWordCount,
    int? DurationSeconds,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StageSpec> Stages);

public sealed record StageSpec(
    string AgentRole,
    string TriggerEventType,
    string PublishEventType,
    IReadOnlyList<string> RequiredAttributes,
    IReadOnlyList<string> RequiredArtifactAttributes,
    IReadOnlyList<string> RequiredArtifactMediaTypes,
    IReadOnlyDictionary<string, string> Requirements,
    IReadOnlyList<string> AcceptanceCriteria);
