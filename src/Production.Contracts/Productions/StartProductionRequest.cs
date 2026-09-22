namespace Beats.Production.Contracts.Productions;

public sealed record StartProductionRequest(
    string Prompt,
    string? Style = null,
    int TargetWordCount = 800,
    int? DurationSeconds = null);
