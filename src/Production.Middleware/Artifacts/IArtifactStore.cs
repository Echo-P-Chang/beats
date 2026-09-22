namespace Beats.Production.Middleware.Artifacts;

public interface IArtifactStore
{
    Task<string> SaveAsync(
        Stream content,
        string relativePath,
        string mediaType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string artifactUri,
        CancellationToken cancellationToken = default);
}
