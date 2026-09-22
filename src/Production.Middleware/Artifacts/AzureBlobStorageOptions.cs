namespace Beats.Production.Middleware.Artifacts;

public sealed class AzureBlobStorageOptions
{
    public const string SectionName = "AzureStorage";

    public string? ConnectionString { get; set; }
    public string? ConnectionStringEnvironmentVariable { get; set; } = "AZURE_STORAGE_CONNECTION_STRING";
    public string AccountName { get; set; } = "devstoreaccount1";
    public string ContainerName { get; set; } = "artifacts";

    public Uri BlobServiceUri => new($"https://{AccountName}.blob.core.windows.net/");

    public string? GetConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.IsNullOrWhiteSpace(ConnectionString) ? null : ConnectionString;
    }
}
