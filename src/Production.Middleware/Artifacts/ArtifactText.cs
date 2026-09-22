using System.Text;

namespace Beats.Production.Middleware.Artifacts;

public static class ArtifactText
{
    public static async Task<string> ReadAsync(
        IArtifactStore artifactStore,
        string artifactUri,
        CancellationToken cancellationToken)
    {
        await using var stream = await artifactStore.OpenReadAsync(artifactUri, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    public static async Task<string> SaveAsync(
        IArtifactStore artifactStore,
        string text,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        return await artifactStore.SaveAsync(
            stream,
            relativePath,
            "text/plain; charset=utf-8",
            cancellationToken);
    }
}
