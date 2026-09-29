namespace Beats.Production.Middleware.Ai;

public sealed record ImageGenerationRequest(
    string Prompt,
    string? NegativePrompt = null,
    int? Width = null,
    int? Height = null,
    int? Steps = null,
    double? Guidance = null,
    ulong? Seed = null,
    string? FileNamePrefix = null);
