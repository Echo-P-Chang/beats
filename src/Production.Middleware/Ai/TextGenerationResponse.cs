namespace Beats.Production.Middleware.Ai;

public sealed record TextGenerationResponse(
    string Text,
    string Model,
    TimeSpan? TotalDuration = null,
    string? FinishReason = null);
