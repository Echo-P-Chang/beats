namespace Beats.Production.Middleware.Ai;

public interface ITextGenerationClient
{
    Task<TextGenerationResponse> GenerateAsync(
        TextGenerationRequest request,
        CancellationToken cancellationToken = default);
}
