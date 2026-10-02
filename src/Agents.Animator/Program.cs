using Beats.Agents.Animator;
using Beats.Production.Contracts;
using Beats.Production.Flows.DependencyInjection;
using Beats.Production.Middleware.Configuration;
using Beats.Production.Middleware.DependencyInjection;
using Microsoft.Extensions.Options;

LocalEnvFile.Load();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<FoundryAnimatorOptions>(
    builder.Configuration.GetSection(FoundryAnimatorOptions.SectionName));
builder.Services.Configure<AzureSoraVideoOptions>(
    builder.Configuration.GetSection(AzureSoraVideoOptions.SectionName));
builder.Services.AddProductionMiddleware();
builder.Services.AddProductionFlows(builder.Configuration);
builder.Services.AddAzureBlobArtifacts(builder.Configuration);
builder.Services.AddProductionDatabase(builder.Configuration);
builder.Services.AddOllamaTextGeneration(builder.Configuration);
builder.Services.AddSingleton<LocalAnimatorAdapter>();
builder.Services.AddSingleton<FoundryAnimatorClient>();
builder.Services.AddHttpClient<AzureSoraVideoAnimatorAdapter>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AzureSoraVideoOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddScoped<IAnimatorAdapter>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FoundryAnimatorOptions>>().Value;

    if (options.Provider.Equals("Foundry", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<FoundryAnimatorClient>();
    }

    if (options.Provider.Equals("Sora", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<AzureSoraVideoAnimatorAdapter>();
    }

    return serviceProvider.GetRequiredService<LocalAnimatorAdapter>();
});
builder.Services.AddRabbitMqMessaging(
    builder.Configuration,
    consumers => consumers.AddFlowConsumersForAgent(builder.Configuration, AgentRoles.Animator, typeof(Program).Assembly));

var host = builder.Build();
host.Run();
