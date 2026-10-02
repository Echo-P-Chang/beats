using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Storyboarding;

namespace Beats.Agents.Animator;

public sealed record AnimationPlanRequest(
    Guid ProductionId,
    string TriggerEventType,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<ArtifactReference> Artifacts,
    IReadOnlyList<ArtifactReference> ImageArtifacts,
    string? StoryText,
    SceneBreakdownDocument? SceneBreakdown,
    AnimationPlanDocument? AnimationPlan);
