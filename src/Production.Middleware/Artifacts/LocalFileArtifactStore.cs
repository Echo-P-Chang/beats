using Microsoft.Extensions.Options;

namespace Beats.Production.Middleware.Artifacts;

public sealed class LocalFileArtifactStore(IOptions<ArtifactStoreOptions> options) : IArtifactStore
{
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(
        Stream content,
        string relativePath,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedRelativePath));

        if (!fullPath.StartsWith(_rootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Artifact path must stay inside the configured artifact root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, cancellationToken);

        return new Uri(fullPath).AbsoluteUri;
    }

    public Task<Stream> OpenReadAsync(
        string artifactUri,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(artifactUri);

        if (!uri.IsFile)
        {
            throw new NotSupportedException("The local artifact store only supports file:// artifact URIs.");
        }

        Stream stream = File.OpenRead(uri.LocalPath);
        return Task.FromResult(stream);
    }
}
