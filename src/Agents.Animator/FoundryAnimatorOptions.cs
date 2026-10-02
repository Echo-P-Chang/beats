namespace Beats.Agents.Animator;

public sealed class FoundryAnimatorOptions
{
    public const string SectionName = "FoundryAnimator";

    public string Provider { get; set; } = "Local";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string AgentId { get; set; } = string.Empty;

    public string AgentName { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "v1";

    public string Model { get; set; } = "beats-animator-gpt-5-nano";

    public int TimeoutSeconds { get; set; } = 300;

    public int MaxAttempts { get; set; } = 3;

    public int RetryDelaySeconds { get; set; } = 45;

    public string TokenScope { get; set; } = "https://ai.azure.com/.default";

    public string? BearerToken { get; set; }

    public string BearerTokenEnvironmentVariable { get; set; } = "FOUNDRY_ANIMATOR_BEARER_TOKEN";
}
