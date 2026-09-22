namespace Beats.Production.Middleware.Persistence;

public sealed record ProductionRecord(
    Guid ProductionId,
    string Prompt,
    string? Style,
    int TargetWordCount,
    int? DurationSeconds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
