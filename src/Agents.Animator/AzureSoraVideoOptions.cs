namespace Beats.Agents.Animator;

public sealed class AzureSoraVideoOptions
{
    public const string SectionName = "AzureSoraVideo";

    public string OpenAiEndpoint { get; set; } = "https://beats-sora-video.openai.azure.com";

    public string DeploymentName { get; set; } = "beats-animator-sora-2";

    public string TokenScope { get; set; } = "https://ai.azure.com/.default";

    public string? BearerToken { get; set; }

    public string BearerTokenEnvironmentVariable { get; set; } = "AZURE_SORA_BEARER_TOKEN";

    public string? ApiKey { get; set; }

    public string ApiKeyEnvironmentVariable { get; set; } = "AZURE_SORA_API_KEY";

    public string BlobServiceEndpoint { get; set; } = "https://storiess.blob.core.windows.net";

    public string InputContainerName { get; set; } = "animator-inputs";

    public string OutputContainerName { get; set; } = "animator-outputs";

    public string? StorageConnectionString { get; set; }

    public string StorageConnectionStringEnvironmentVariable { get; set; } = "AZURE_ANIMATOR_STORAGE_CONNECTION_STRING";

    public string Size { get; set; } = "720x1280";

    public string Seconds { get; set; } = "4";

    public int MaxClipSeconds { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 420;

    public int PollIntervalSeconds { get; set; } = 20;
}
