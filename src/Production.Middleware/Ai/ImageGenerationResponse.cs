namespace Beats.Production.Middleware.Ai;

public sealed record ImageGenerationResponse(
    byte[] Content,
    string MediaType,
    string FileName,
    string? Subfolder,
    string OutputType,
    string PromptId,
    TimeSpan TotalDuration);
