using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Beats.Production.Contracts.Specifications;
using Beats.Production.Middleware.Artifacts;

namespace Beats.Production.Middleware.Specifications;

public sealed class ProductionSpecStore(IArtifactStore artifactStore) : IProductionSpecStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public async Task<string> SaveAsync(
        ProductionSpec spec,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(spec, JsonOptions);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return await artifactStore.SaveAsync(
            stream,
            relativePath,
            "application/json; charset=utf-8",
            cancellationToken);
    }

    public async Task<ProductionSpec> LoadAsync(
        string specUri,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await artifactStore.OpenReadAsync(specUri, cancellationToken);
        var spec = await JsonSerializer.DeserializeAsync<ProductionSpec>(
            stream,
            JsonOptions,
            cancellationToken);

        return spec ?? throw new InvalidOperationException("Production spec artifact is empty or invalid.");
    }
}
