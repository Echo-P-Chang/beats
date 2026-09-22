using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
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

app.MapPost("/productions", async (
    StartProductionRequest request,
    IEventPublisher eventPublisher,
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

    var message = EventEnvelope<ProductionRequestedPayload>.Create(
        EventTypes.ProductionRequested,
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

app.Run();
