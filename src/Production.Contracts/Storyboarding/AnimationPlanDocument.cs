namespace Beats.Production.Contracts.Storyboarding;

public sealed record AnimationPlanDocument(
    Guid ProductionId,
    string SourceSceneBreakdownUri,
    int? TotalDurationSeconds,
    IReadOnlyList<AnimationShot> Shots);

public sealed record AnimationShot(
    string SceneId,
    int Order,
    string Title,
    int DurationSeconds,
    string Motion,
    string Camera,
    string Transition,
    string Notes);
