using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Beats.Production.Middleware.Artifacts;

public sealed class AzureBlobArtifactStore(IOptions<AzureBlobStorageOptions> options) : IArtifactStore
{
    private readonly AzureBlobStorageOptions _options = options.Value;
    private readonly DefaultAzureCredential _credential = new();

    public async Task<string> SaveAsync(
        Stream content,
        string relativePath,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        var blobName = NormalizeBlobName(relativePath);
        var blob = GetContainerClient().GetBlobClient(blobName);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = mediaType
                }
            },
            cancellationToken);

        return blob.Uri.ToString();
    }

    public async Task<Stream> OpenReadAsync(
        string artifactUri,
        CancellationToken cancellationToken = default)
    {
        var blob = GetBlobClient(artifactUri);
        var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);

        return response.Value.Content;
    }

    private BlobContainerClient GetContainerClient()
    {
        var connectionString = _options.GetConnectionString();

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var serviceFromConnectionString = new BlobServiceClient(connectionString);
            return serviceFromConnectionString.GetBlobContainerClient(_options.ContainerName);
        }

        var service = new BlobServiceClient(_options.BlobServiceUri, _credential);
        return service.GetBlobContainerClient(_options.ContainerName);
    }

    private BlobClient GetBlobClient(string artifactUri)
    {
        var connectionString = _options.GetConnectionString();

        if (!string.IsNullOrWhiteSpace(connectionString) &&
            Uri.TryCreate(artifactUri, UriKind.Absolute, out var uri))
        {
            var container = GetContainerClient();
            var marker = $"/{_options.ContainerName}/";
            var absolutePath = uri.AbsolutePath;
            var markerIndex = absolutePath.IndexOf(marker, StringComparison.Ordinal);

            if (markerIndex >= 0)
            {
                var blobName = Uri.UnescapeDataString(absolutePath[(markerIndex + marker.Length)..]);
                return container.GetBlobClient(blobName);
            }
        }

        return new BlobClient(new Uri(artifactUri), _credential);
    }

    private static string NormalizeBlobName(string relativePath)
    {
        var blobName = relativePath.Replace('\\', '/').TrimStart('/');

        if (blobName.Contains("../", StringComparison.Ordinal) ||
            blobName.Equals("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Artifact path must stay inside the artifact container.");
        }

        return blobName;
    }
}
