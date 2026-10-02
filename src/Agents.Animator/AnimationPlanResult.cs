namespace Beats.Agents.Animator;

public sealed record AnimationPlanResult(
    string Content,
    string Summary,
    int AnimationCount,
    IReadOnlyList<GeneratedAnimationArtifact> GeneratedArtifacts);
