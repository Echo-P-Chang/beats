using Beats.Agents.Editor;
using Beats.Production.Contracts;
using Beats.Production.Flows.DependencyInjection;
using Beats.Production.Middleware.Configuration;
using Beats.Production.Middleware.DependencyInjection;

LocalEnvFile.Load();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddProductionMiddleware();
builder.Services.AddProductionFlows(builder.Configuration);
builder.Services.AddAzureBlobArtifacts(builder.Configuration);
builder.Services.AddProductionDatabase(builder.Configuration);
builder.Services.AddRabbitMqMessaging(
    builder.Configuration,
    consumers => consumers.AddFlowConsumersForAgent(builder.Configuration, AgentRoles.Editor, typeof(Program).Assembly));

var host = builder.Build();
host.Run();
