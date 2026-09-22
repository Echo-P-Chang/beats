namespace Beats.Production.Contracts.Events.Payloads;

public sealed record ProductionRequestedPayload(
    string Prompt,
    string? Style,
    int TargetWordCount,
    int? DurationSeconds);
