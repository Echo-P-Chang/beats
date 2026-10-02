using System.Text.Json;
using System.Text.Encodings.Web;

namespace Beats.Production.Middleware.Artifacts;

public static class ArtifactJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static async Task<T> ReadAsync<T>(
        IArtifactStore artifactStore,
        string artifactUri,
        CancellationToken cancellationToken)
    {
        await using var stream = await artifactStore.OpenReadAsync(artifactUri, cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonOptions,
            cancellationToken);

        return value ?? throw new InvalidOperationException(
            $"JSON artifact '{artifactUri}' is empty or invalid.");
    }

    public static async Task<string> SaveAsync<T>(
        IArtifactStore artifactStore,
        T value,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            JsonOptions,
            cancellationToken);
        stream.Position = 0;

        return await artifactStore.SaveAsync(
            stream,
            relativePath,
            "application/json; charset=utf-8",
            cancellationToken);
    }
}
