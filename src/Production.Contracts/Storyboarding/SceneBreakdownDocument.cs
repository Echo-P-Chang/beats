namespace Beats.Production.Contracts.Storyboarding;

public sealed record SceneBreakdownDocument(
    Guid ProductionId,
    string Title,
    string Style,
    string StoryUri,
    IReadOnlyList<SceneBreakdownItem> Scenes);

public sealed record SceneBreakdownItem(
    string SceneId,
    int Order,
    string Title,
    string VisualDescription,
    string CharacterAction,
    string MoodAndColor,
    string IllustratorPrompt,
    string AnimatorPrompt);
