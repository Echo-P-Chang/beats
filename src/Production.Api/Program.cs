using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Flows;
using Beats.Production.Flows.DependencyInjection;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Configuration;
using Beats.Production.Middleware.DependencyInjection;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;

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

app.MapPost("/productions", async (
    StartProductionRequest request,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
    IProductionRepository productionRepository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest("Prompt is required.");
    }

    var productionId = Guid.NewGuid();
    var payload = new ProductionRequestedPayload(
        request.Prompt,
        request.Style,
        request.TargetWordCount,
        request.DurationSeconds);

    var publication = productionFlow.GetRequiredPublication<ProductionRequestedPayload>(
        AgentRoles.ProductionApi);

    var message = EventEnvelope<ProductionRequestedPayload>.Create(
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

app.Run();

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
