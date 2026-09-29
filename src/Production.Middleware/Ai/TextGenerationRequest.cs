namespace Beats.Production.Middleware.Ai;

public sealed record TextGenerationRequest(
    string Prompt,
    string? SystemPrompt = null,
    string? Model = null,
    double? Temperature = null,
    int? NumPredict = null);
