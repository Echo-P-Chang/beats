using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Contracts.Media;
using Beats.Production.Flows;
using Beats.Production.Flows.DependencyInjection;
using Beats.Production.Flows.FlowConfiguration;
using Beats.Production.Contracts.Productions;
using Beats.Production.Contracts.Specifications;
using Beats.Production.Middleware.Ai;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Configuration;
using Beats.Production.Middleware.DependencyInjection;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using Beats.Production.Middleware.Specifications;
using Microsoft.Extensions.Options;

LocalEnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProductionMiddleware(options =>
{
    options.RootPath = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, "..", "..", "artifacts"));
});
builder.Services.AddProductionFlows(builder.Configuration);
builder.Services.AddAzureBlobArtifacts(builder.Configuration);
builder.Services.AddProductionDatabase(builder.Configuration);
builder.Services.AddOllamaTextGeneration(builder.Configuration);
builder.Services.AddRabbitMqMessaging(builder.Configuration);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    service = "Beats Production API",
    status = "ready"
}));

app.MapGet("/production-flow", (IConfiguration configuration) =>
{
    var path = ResolveProductionFlowPath(
        configuration["ProductionFlows:ConfigPath"] ?? "flows/production-flow.json");

    return Results.Text(File.ReadAllText(path), "application/json");
})
.WithName("GetProductionFlow");

app.MapGet("/production-flow/designer", (IConfiguration configuration) =>
{
    var path = ResolveProductionFlowPath(
        configuration["ProductionFlows:ConfigPath"] ?? "flows/production-flow.json");
    var json = File.ReadAllText(path);
    var validation = ProductionFlowDesignerSource.ValidateJson(json);

    if (!validation.IsValid)
    {
        return Results.BadRequest(new
        {
            configPath = path,
            validation
        });
    }

    return Results.Ok(new
    {
        configPath = path,
        flow = ProductionFlowDesignerSource.CreateDesignerModel(json),
        validation
    });
})
.WithName("GetProductionFlowDesigner");

app.MapPost("/production-flow/validate", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    var flowJson = ExtractFlowJson(body);

    if (string.IsNullOrWhiteSpace(flowJson))
    {
        return Results.BadRequest(new ProductionFlowValidationResult(
            false,
            ["Request body must contain a flow JSON document or a flowJson string property."]));
    }

    var validation = ProductionFlowDesignerSource.ValidateJson(flowJson);

    return validation.IsValid
        ? Results.Ok(validation)
        : Results.BadRequest(validation);
})
.WithName("ValidateProductionFlow");

app.MapPost("/ai/text-generations", async (
    TextGenerationRequest request,
    ITextGenerationClient textGenerationClient,
    CancellationToken cancellationToken) =>
{
    var response = await textGenerationClient.GenerateAsync(request, cancellationToken);
    return Results.Ok(response);
})
.WithName("GenerateText");

app.MapPost("/productions", async (
    StartProductionRequest request,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
    IProductionSpecStore productionSpecStore,
    IProductionRepository productionRepository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest("Prompt is required.");
    }

    var productionId = Guid.NewGuid();
    var productionSpec = CreateProductionSpec(productionId, request);
    var specUri = await productionSpecStore.SaveAsync(
        productionSpec,
        $"productions/{productionId}/00-spec/production-spec.json",
        cancellationToken);
    var specArtifact = new ArtifactReference(
        specUri,
        "application/json",
        "Production requirement spec used by each agent for preflight validation.");

    var payload = CommonPayload.Create(
        EventTypes.ProductionRequested,
        AgentRoles.ProductionApi,
        attributes: new Dictionary<string, string>
        {
            ["specUri"] = specUri,
            ["specVersion"] = productionSpec.SpecVersion.ToString(),
            ["prompt"] = request.Prompt,
            ["style"] = request.Style ?? string.Empty,
            ["targetWordCount"] = request.TargetWordCount.ToString(),
            ["durationSeconds"] = request.DurationSeconds?.ToString() ?? string.Empty
        });

    var publication = productionFlow.GetRequiredPublication<CommonPayload>(
        AgentRoles.ProductionApi);

    var message = EventEnvelope<CommonPayload>.Create(
        publication.EventType,
        productionId,
        AgentRoles.ProductionApi,
        payload);

    await productionRepository.CreateProductionAsync(
        productionId,
        request.Prompt,
        request.Style,
        request.TargetWordCount,
        request.DurationSeconds,
        ProductionStatus.Requested.ToString(),
        cancellationToken);

    await productionRepository.RecordArtifactAsync(
        productionId,
        AgentRoles.ProductionApi,
        specArtifact,
        cancellationToken);

    await productionRepository.RecordEventAsync(message, cancellationToken);
    await eventPublisher.PublishAsync(message, cancellationToken);

    return Results.Accepted(
        $"/productions/{productionId}",
        new StartProductionResponse(
            productionId,
            message.EventId,
            ProductionStatus.Requested.ToString()));
})
.WithName("StartProduction");

app.MapGet("/productions/{productionId:guid}", async (
    Guid productionId,
    IProductionRepository productionRepository,
    CancellationToken cancellationToken) =>
{
    var production = await productionRepository.GetProductionAsync(productionId, cancellationToken);

    return production is null
        ? Results.NotFound()
        : Results.Ok(new
    {
        production.ProductionId,
        production.Status,
        production.Prompt,
        production.Style,
        production.TargetWordCount,
        production.DurationSeconds,
        production.CreatedAt,
        production.UpdatedAt
    });
})
.WithName("GetProduction");

app.MapGet("/productions/{productionId:guid}/events", async (
    Guid productionId,
    IProductionRepository productionRepository,
    CancellationToken cancellationToken) =>
{
    var production = await productionRepository.GetProductionAsync(productionId, cancellationToken);

    if (production is null)
    {
        return Results.NotFound();
    }

    var events = await productionRepository.GetProductionEventsAsync(productionId, cancellationToken);

    return Results.Ok(events.Select(productionEvent => new
    {
        productionEvent.EventId,
        productionEvent.ProductionId,
        productionEvent.EventType,
        productionEvent.CorrelationId,
        productionEvent.CausationId,
        productionEvent.Producer,
        productionEvent.SchemaVersion,
        productionEvent.CreatedAt
    }));
})
.WithName("GetProductionEvents");

app.MapGet("/productions/{productionId:guid}/artifacts", async (
    Guid productionId,
    IProductionRepository productionRepository,
    CancellationToken cancellationToken) =>
{
    var production = await productionRepository.GetProductionAsync(productionId, cancellationToken);

    if (production is null)
    {
        return Results.NotFound();
    }

    var artifacts = await productionRepository.GetProductionArtifactsAsync(productionId, cancellationToken);

    return Results.Ok(artifacts.Select(artifact =>
    {
        var artifactPath = GetArtifactPath(productionId, artifact.ArtifactUri);

        return new
        {
            artifact.ArtifactId,
            artifact.ProductionId,
            artifact.AgentRole,
            artifact.ArtifactUri,
            artifact.MediaType,
            artifact.Description,
            artifact.CreatedAt,
            ArtifactPath = artifactPath,
            DownloadUrl = artifactPath is null
                ? null
                : $"/productions/{productionId}/{artifactPath}"
        };
    }));
})
.WithName("GetProductionArtifacts");

app.MapMethods("/productions/{productionId:guid}/{*artifactPath}", new[] { HttpMethods.Get, HttpMethods.Head }, async (
    Guid productionId,
    string artifactPath,
    IArtifactStore artifactStore,
    IOptions<AzureBlobStorageOptions> storageOptions,
    IProductionRepository productionRepository,
    CancellationToken cancellationToken) =>
{
    var production = await productionRepository.GetProductionAsync(productionId, cancellationToken);

    if (production is null)
    {
        return Results.NotFound();
    }

    var normalizedPath = NormalizeArtifactPath(artifactPath);

    if (normalizedPath is null)
    {
        return Results.BadRequest("Artifact path is invalid.");
    }

    var relativePath = $"productions/{productionId}/{normalizedPath}";
    var artifactUri = BuildArtifactUri(storageOptions.Value, relativePath);

    try
    {
        var stream = await artifactStore.OpenReadAsync(artifactUri, cancellationToken);
        return Results.Stream(
            stream,
            GetContentType(normalizedPath),
            enableRangeProcessing: true);
    }
    catch (Azure.RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
    {
        return Results.NotFound();
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
})
.WithName("GetProductionArtifact");

app.Run();

static ProductionSpec CreateProductionSpec(
    Guid productionId,
    StartProductionRequest request)
{
    return new ProductionSpec(
        productionId,
        SpecVersion: 1,
        request.Prompt,
        request.Style,
        request.TargetWordCount,
        request.DurationSeconds,
        DateTimeOffset.UtcNow,
        [
            new StageSpec(
                AgentRoles.Storyteller,
                EventTypes.ProductionRequested,
                EventTypes.StoryCreated,
                RequiredAttributes: ["specUri", "prompt", "targetWordCount"],
                RequiredArtifactAttributes: [],
                RequiredArtifactMediaTypes: [],
                Requirements: new Dictionary<string, string>
                {
                    ["language"] = "Taiwan Traditional Chinese",
                    ["storyLength"] = $"About {request.TargetWordCount} words",
                    ["sceneBreakdown"] = "Exactly 3 scenes with visual and animation guidance"
                },
                AcceptanceCriteria:
                [
                    "Story has a complete beginning, middle, and ending.",
                    "Scene breakdown is present and complete.",
                    "Story artifact URI is published as storyUri.",
                    "Scene breakdown JSON is published as sceneBreakdownUri.",
                    "Animation plan JSON is published as animationPlanUri."
                ]),
            new StageSpec(
                AgentRoles.Illustrator,
                EventTypes.StoryCreated,
                EventTypes.SceneImagesCreated,
                RequiredAttributes: ["specUri", "storyUri", "sceneBreakdownUri", "animationPlanUri"],
                RequiredArtifactAttributes: ["storyUri", "sceneBreakdownUri", "animationPlanUri"],
                RequiredArtifactMediaTypes: ["text/plain", "application/json"],
                Requirements: new Dictionary<string, string>
                {
                    ["style"] = request.Style ?? "watercolor storybook",
                    ["consistency"] = "All scene images must use the same visual style",
                    ["coverage"] = "Create one image for each scene"
                },
                AcceptanceCriteria:
                [
                    "Every scene has an image artifact.",
                    "Scene image artifact media types are image/*.",
                    "Image manifest JSON is published as imageManifestUri.",
                    "SceneImagesCreated includes imageCount."
                ]),
            new StageSpec(
                AgentRoles.Animator,
                EventTypes.SceneImagesCreated,
                EventTypes.SceneAnimationsCreated,
                RequiredAttributes: ["specUri", "sceneBreakdownUri", "animationPlanUri", "imageManifestUri", "imageCount"],
                RequiredArtifactAttributes: ["imageManifestUri"],
                RequiredArtifactMediaTypes: ["image/*"],
                Requirements: new Dictionary<string, string>
                {
                    ["motion"] = "Use the Storyteller scene breakdown and animation plan with the generated scene images",
                    ["duration"] = request.DurationSeconds?.ToString() ?? "not specified",
                    ["output"] = "Publish an animator execution report and, when available, a video artifact"
                },
                AcceptanceCriteria:
                [
                    "Animator execution report is published as animationReportUri.",
                    "animationCount is published.",
                    "Generated video artifact is written back to the local artifact store when available."
                ]),
            new StageSpec(
                AgentRoles.Editor,
                EventTypes.SceneAnimationsCreated,
                EventTypes.FinalVideoCreated,
                RequiredAttributes: ["specUri", "animationReportUri", "animationCount"],
                RequiredArtifactAttributes: ["animationReportUri"],
                RequiredArtifactMediaTypes: ["text/plain"],
                Requirements: new Dictionary<string, string>
                {
                    ["editing"] = "Assemble generated animation video when available, otherwise publish a final cut plan",
                    ["voiceover"] = "Prepare narration direction that matches story beats"
                },
                AcceptanceCriteria:
                [
                    "Final video or final manuscript artifact is published as videoUri.",
                    "Voiceover artifact URI is published as voiceOverUri."
                ]),
            new StageSpec(
                AgentRoles.Reviewer,
                EventTypes.FinalVideoCreated,
                EventTypes.ReviewPassed,
                RequiredAttributes: ["specUri", "videoUri"],
                RequiredArtifactAttributes: ["videoUri"],
                RequiredArtifactMediaTypes: [],
                Requirements: new Dictionary<string, string>
                {
                    ["qualityGate"] = "Confirm required outputs exist and match the production spec"
                },
                AcceptanceCriteria:
                [
                    "Review report artifact is published as reviewReportUri.",
                    "reviewPassed is true when the quality gate passes."
                ])
        ]);
}

static string? NormalizeArtifactPath(string artifactPath)
{
    var normalized = artifactPath.Replace('\\', '/').TrimStart('/');

    if (string.IsNullOrWhiteSpace(normalized) ||
        normalized.Equals("..", StringComparison.Ordinal) ||
        normalized.Contains("../", StringComparison.Ordinal) ||
        normalized.Contains("/..", StringComparison.Ordinal))
    {
        return null;
    }

    return normalized;
}

static string? GetArtifactPath(Guid productionId, string artifactUri)
{
    var marker = $"/productions/{productionId}/";

    if (Uri.TryCreate(artifactUri, UriKind.Absolute, out var uri))
    {
        var markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex >= 0)
        {
            return Uri.UnescapeDataString(uri.AbsolutePath[(markerIndex + marker.Length)..]);
        }
    }

    var textIndex = artifactUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

    return textIndex >= 0
        ? artifactUri[(textIndex + marker.Length)..]
        : null;
}

static string ExtractFlowJson(string body)
{
    if (string.IsNullOrWhiteSpace(body))
    {
        return string.Empty;
    }

    try
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);

        if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
            document.RootElement.TryGetProperty("flowJson", out var flowJsonProperty) &&
            flowJsonProperty.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return flowJsonProperty.GetString() ?? string.Empty;
        }
    }
    catch (System.Text.Json.JsonException)
    {
        return body;
    }

    return body;
}

static string BuildArtifactUri(AzureBlobStorageOptions options, string relativePath)
{
    var blobName = string.Join(
        '/',
        relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    var connectionString = options.GetConnectionString();

    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        var blobEndpoint = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts => parts[0].Equals("BlobEndpoint", StringComparison.OrdinalIgnoreCase))?[1];

        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            return $"{blobEndpoint.TrimEnd('/')}/{options.ContainerName}/{blobName}";
        }
    }

    return new Uri(
        options.BlobServiceUri,
        $"{options.ContainerName}/{blobName}").ToString();
}

static string GetContentType(string artifactPath)
{
    return Path.GetExtension(artifactPath).ToLowerInvariant() switch
    {
        ".txt" => "text/plain; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".md" => "text/markdown; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        _ => "application/octet-stream"
    };
}

static string ResolveProductionFlowPath(string configuredPath)
{
    if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
    {
        return configuredPath;
    }

    foreach (var root in CandidateRoots())
    {
        var candidate = Path.GetFullPath(Path.Combine(root, configuredPath));
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new FileNotFoundException($"Flow config file '{configuredPath}' was not found.");
}

static IEnumerable<string> CandidateRoots()
{
    var roots = new[]
    {
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory
    };

    foreach (var root in roots)
    {
        var current = new DirectoryInfo(root);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
