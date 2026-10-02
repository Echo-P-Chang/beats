using System.Text;
using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Beats.Agents.Animator;

public sealed class FoundryAnimatorClient(
    IOptions<FoundryAnimatorOptions> options,
    ILogger<FoundryAnimatorClient> logger) : IAnimatorAdapter
{
    private readonly FoundryAnimatorOptions _options = options.Value;

    public async Task<AnimationPlanResult> CreateAnimationPlanAsync(
        AnimationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await CreateAnimationPlanAttemptAsync(request, attempt, cancellationToken);
            }
            catch (InvalidOperationException exception) when (
                IsRateLimitFailure(exception) &&
                attempt < maxAttempts)
            {
                lastException = exception;
                var retryDelay = TimeSpan.FromSeconds(Math.Max(1, _options.RetryDelaySeconds));

                logger.LogWarning(
                    exception,
                    "Azure Foundry Animator rate limit hit. ProductionId={ProductionId}, Attempt={Attempt}/{MaxAttempts}. Retrying after {RetryDelay}.",
                    request.ProductionId,
                    attempt,
                    maxAttempts,
                    retryDelay);

                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Azure Foundry Animator failed before starting a run.");
    }

    private async Task<AnimationPlanResult> CreateAnimationPlanAttemptAsync(
        AnimationPlanRequest request,
        int attempt,
        CancellationToken cancellationToken)
    {
        var client = new PersistentAgentsClient(_options.ProjectEndpoint, BuildCredential());
        var prompt = BuildPrompt(request);

        logger.LogInformation(
            "Calling Azure Foundry Animator agent. ProductionId={ProductionId}, AgentId={AgentId}, AgentName={AgentName}, Attempt={Attempt}, ArtifactCount={ArtifactCount}, ImageCount={ImageCount}",
            request.ProductionId,
            _options.AgentId,
            _options.AgentName,
            attempt,
            request.Artifacts.Count,
            request.ImageArtifacts.Count);

        PersistentAgentThread thread = await client.Threads.CreateThreadAsync(
            cancellationToken: cancellationToken);

        await client.Messages.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            prompt,
            cancellationToken: cancellationToken);

        ThreadRun run = await client.Runs.CreateRunAsync(
            thread.Id,
            _options.AgentId,
            additionalInstructions: BuildRunInstructions(),
            cancellationToken: cancellationToken);

        run = await WaitForRunAsync(client, thread.Id, run, cancellationToken);

        if (run.Status != RunStatus.Completed)
        {
            throw new InvalidOperationException(
                $"Azure Foundry Animator run did not complete. Status={run.Status}, LastError={run.LastError?.Message ?? "none"}");
        }

        var animationPlan = await ReadAgentReplyAsync(
            client,
            thread.Id,
            run.Id,
            cancellationToken);
        var content = BuildArtifactContent(request, thread.Id, run.Id, animationPlan);

        return new AnimationPlanResult(
            content,
            $"Generated animation plan with Azure Foundry Animator agent '{_options.AgentName}'. ThreadId={thread.Id}, RunId={run.Id}.",
            Math.Max(1, request.ImageArtifacts.Count),
            []);
    }

    private async Task<ThreadRun> WaitForRunAsync(
        PersistentAgentsClient client,
        string threadId,
        ThreadRun run,
        CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(_options.TimeoutSeconds);

        while (run.Status == RunStatus.Queued ||
               run.Status == RunStatus.InProgress ||
               run.Status == RunStatus.RequiresAction)
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new TimeoutException(
                    $"Azure Foundry Animator run timed out after {_options.TimeoutSeconds} seconds. ThreadId={threadId}, RunId={run.Id}, Status={run.Status}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            run = await client.Runs.GetRunAsync(threadId, run.Id, cancellationToken);
        }

        return run;
    }

    private static async Task<string> ReadAgentReplyAsync(
        PersistentAgentsClient client,
        string threadId,
        string runId,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        AsyncPageable<PersistentThreadMessage> messages = client.Messages.GetMessagesAsync(
            threadId: threadId,
            runId: runId,
            order: ListSortOrder.Ascending,
            cancellationToken: cancellationToken);

        await foreach (var message in messages.WithCancellation(cancellationToken))
        {
            if (message.Role != MessageRole.Agent)
            {
                continue;
            }

            foreach (var contentItem in message.ContentItems)
            {
                if (contentItem is MessageTextContent textContent)
                {
                    text.AppendLine(textContent.Text);
                }
            }
        }

        return text.Length > 0
            ? text.ToString()
            : "Azure Foundry Animator completed the run but returned no text content.";
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProjectEndpoint))
        {
            throw new InvalidOperationException("FoundryAnimator:ProjectEndpoint is required when Foundry animator is enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.AgentId))
        {
            throw new InvalidOperationException("FoundryAnimator:AgentId is required when Foundry animator is enabled.");
        }
    }

    private TokenCredential BuildCredential()
    {
        var configuredToken = _options.BearerToken;

        if (string.IsNullOrWhiteSpace(configuredToken) &&
            !string.IsNullOrWhiteSpace(_options.BearerTokenEnvironmentVariable))
        {
            configuredToken = Environment.GetEnvironmentVariable(_options.BearerTokenEnvironmentVariable);
        }

        return string.IsNullOrWhiteSpace(configuredToken)
            ? BuildEntraCredential()
            : new StaticBearerTokenCredential(configuredToken);
    }

    private TokenCredential BuildEntraCredential()
    {
        var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");

        if (!string.IsNullOrWhiteSpace(tenantId) &&
            !string.IsNullOrWhiteSpace(clientId) &&
            !string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ScopedTokenCredential(
                new ClientSecretCredential(tenantId, clientId, clientSecret),
                _options.TokenScope);
        }

        return new ScopedTokenCredential(
            new DefaultAzureCredential(),
            _options.TokenScope);
    }

    private static string BuildPrompt(AnimationPlanRequest request)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("You are the Animator agent in an event-driven AI production pipeline.");
        prompt.AppendLine("Use the supplied Storyteller scene breakdown and animation plan as the source of truth.");
        prompt.AppendLine("Return an execution report for the animation stage. Include what video artifacts should be generated, which images are used, and any production notes.");
        prompt.AppendLine("Do not create a new story or a new animation plan. Do not claim that you generated final video pixels unless a tool actually created video artifacts.");
        prompt.AppendLine();
        prompt.AppendLine($"ProductionId: {request.ProductionId}");
        prompt.AppendLine($"TriggerEventType: {request.TriggerEventType}");
        prompt.AppendLine();
        prompt.AppendLine("Attributes:");

        foreach (var attribute in request.Attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal))
        {
            prompt.AppendLine($"- {attribute.Key}: {attribute.Value}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Artifacts:");

        foreach (var artifact in request.Artifacts)
        {
            prompt.AppendLine($"- Uri: {artifact.Uri}");
            prompt.AppendLine($"  MediaType: {artifact.MediaType}");

            if (!string.IsNullOrWhiteSpace(artifact.Description))
            {
                prompt.AppendLine($"  Description: {artifact.Description}");
            }
        }

        if (request.SceneBreakdown is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine("Scene breakdown:");

            foreach (var scene in request.SceneBreakdown.Scenes.OrderBy(scene => scene.Order))
            {
                prompt.AppendLine($"- {scene.SceneId}: {scene.Title}");
                prompt.AppendLine($"  Visual: {scene.VisualDescription}");
                prompt.AppendLine($"  Action: {scene.CharacterAction}");
                prompt.AppendLine($"  Mood: {scene.MoodAndColor}");
                prompt.AppendLine($"  Animator prompt: {scene.AnimatorPrompt}");
            }
        }

        if (request.AnimationPlan is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine("Storyteller animation plan:");

            foreach (var shot in request.AnimationPlan.Shots.OrderBy(shot => shot.Order))
            {
                prompt.AppendLine($"- {shot.SceneId}: {shot.Title}, {shot.DurationSeconds}s");
                prompt.AppendLine($"  Motion: {shot.Motion}");
                prompt.AppendLine($"  Camera: {shot.Camera}");
                prompt.AppendLine($"  Transition: {shot.Transition}");
                prompt.AppendLine($"  Notes: {shot.Notes}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Image artifacts to animate:");

        for (var index = 0; index < request.ImageArtifacts.Count; index++)
        {
            var image = request.ImageArtifacts[index];
            prompt.AppendLine($"- Scene {index + 1:000}");
            prompt.AppendLine($"  Uri: {image.Uri}");
            prompt.AppendLine($"  MediaType: {image.MediaType}");

            if (!string.IsNullOrWhiteSpace(image.Description))
            {
                prompt.AppendLine($"  Description: {image.Description}");
            }
        }

        return prompt.ToString();
    }

    private static bool IsRateLimitFailure(Exception exception)
    {
        return exception.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRunInstructions()
    {
        return """
            You are running as the hosted Animator implementation for Beats Production.
            Your output will be stored by the local worker as an artifact and then passed to the Editor agent.
            Keep the plan actionable, structured, and compact.
            """;
    }

    private static string BuildArtifactContent(
        AnimationPlanRequest request,
        string threadId,
        string runId,
        string animationPlan)
    {
        var content = new StringBuilder();

        content.AppendLine($"# Production {request.ProductionId}");
        content.AppendLine();
        content.AppendLine("## Animator");
        content.AppendLine("Animation execution report generated by Azure Foundry Agent Service.");
        content.AppendLine($"Foundry thread: {threadId}");
        content.AppendLine($"Foundry run: {runId}");
        content.AppendLine();
        content.AppendLine(animationPlan.Trim());
        content.AppendLine();
        content.AppendLine("Input image artifacts:");

        foreach (var image in request.ImageArtifacts)
        {
            content.AppendLine($"- {image.Uri}");
        }

        return content.ToString();
    }

    private sealed class StaticBearerTokenCredential(string token) : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return CreateToken();
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(CreateToken());
        }

        private AccessToken CreateToken()
        {
            return new AccessToken(token, DateTimeOffset.UtcNow.AddMinutes(55));
        }
    }

    private sealed class ScopedTokenCredential(
        TokenCredential credential,
        string tokenScope) : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return credential.GetToken(
                new TokenRequestContext([tokenScope]),
                cancellationToken);
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return credential.GetTokenAsync(
                new TokenRequestContext([tokenScope]),
                cancellationToken);
        }
    }
}
