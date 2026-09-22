using Beats.Agents.Reviewer;
using Beats.Production.Middleware.Configuration;
using Beats.Production.Middleware.DependencyInjection;

LocalEnvFile.Load();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddProductionMiddleware();
builder.Services.AddAzureBlobArtifacts(builder.Configuration);
builder.Services.AddProductionDatabase(builder.Configuration);
builder.Services.AddRabbitMqMessaging(
    builder.Configuration,
    consumers => consumers.AddConsumer<FinalVideoCreatedConsumer>());

var host = builder.Build();
host.Run();
