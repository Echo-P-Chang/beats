namespace Beats.Production.Middleware.Ai;

public interface IImageGenerationClient
{
    Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default);
}
