using Beats.Production.Middleware.Ai;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Beats.Production.Middleware.DependencyInjection;

public static class ProductionMiddlewareServiceCollectionExtensions
{
    public static IServiceCollection AddProductionMiddleware(
        this IServiceCollection services,
        Action<ArtifactStoreOptions>? configureArtifactStore = null,
        Action<RabbitMqOptions>? configureRabbitMq = null)
    {
        if (configureArtifactStore is not null)
        {
            services.Configure(configureArtifactStore);
        }
        else
        {
            services.Configure<ArtifactStoreOptions>(_ => { });
        }

        if (configureRabbitMq is not null)
        {
            services.Configure(configureRabbitMq);
        }
        else
        {
            services.Configure<RabbitMqOptions>(_ => { });
        }

        services.AddSingleton<IArtifactStore, LocalFileArtifactStore>();
        services.AddSingleton<IEventPublisher, LoggingEventPublisher>();

        return services;
    }

    public static IServiceCollection AddAzureBlobArtifacts(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureBlobStorageOptions>(options =>
        {
            var section = configuration.GetSection(AzureBlobStorageOptions.SectionName);

            options.AccountName = section[nameof(AzureBlobStorageOptions.AccountName)] ?? options.AccountName;
            options.ContainerName = section[nameof(AzureBlobStorageOptions.ContainerName)] ?? options.ContainerName;
            options.ConnectionString = section[nameof(AzureBlobStorageOptions.ConnectionString)];
            options.ConnectionStringEnvironmentVariable =
                section[nameof(AzureBlobStorageOptions.ConnectionStringEnvironmentVariable)] ??
                options.ConnectionStringEnvironmentVariable;
        });

        services.AddSingleton<IArtifactStore, AzureBlobArtifactStore>();

        return services;
    }

    public static IServiceCollection AddProductionDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(options =>
        {
            var section = configuration.GetSection(DatabaseOptions.SectionName);

            options.Provider = section[nameof(DatabaseOptions.Provider)] ?? options.Provider;
            options.ConnectionString = section[nameof(DatabaseOptions.ConnectionString)];
            options.ConnectionStringEnvironmentVariable =
                section[nameof(DatabaseOptions.ConnectionStringEnvironmentVariable)];
        });

        var provider = configuration
            .GetSection(DatabaseOptions.SectionName)[nameof(DatabaseOptions.Provider)] ??
            "MySql";

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IProductionRepository, SqlProductionRepository>();
        }
        else
        {
            services.AddSingleton<IProductionRepository, MySqlProductionRepository>();
        }

        return services;
    }

    public static IServiceCollection AddOllamaTextGeneration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(options =>
        {
            var section = configuration.GetSection(OllamaOptions.SectionName);

            options.BaseUrl = section[nameof(OllamaOptions.BaseUrl)] ?? options.BaseUrl;
            options.Model = section[nameof(OllamaOptions.Model)] ?? options.Model;

            if (int.TryParse(section[nameof(OllamaOptions.TimeoutSeconds)], out var timeoutSeconds))
            {
                options.TimeoutSeconds = timeoutSeconds;
            }

            if (double.TryParse(section[nameof(OllamaOptions.Temperature)], out var temperature))
            {
                options.Temperature = temperature;
            }

            if (int.TryParse(section[nameof(OllamaOptions.NumPredict)], out var numPredict))
            {
                options.NumPredict = numPredict;
            }
        });

        services.AddHttpClient<ITextGenerationClient, OllamaTextGenerationClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }

    public static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.Configure<RabbitMqOptions>(options =>
        {
            var section = configuration.GetSection(RabbitMqOptions.SectionName);

            options.Host = section[nameof(RabbitMqOptions.Host)] ?? options.Host;
            options.VirtualHost = section[nameof(RabbitMqOptions.VirtualHost)] ?? options.VirtualHost;
            options.Username = section[nameof(RabbitMqOptions.Username)] ?? options.Username;
            options.Password = section[nameof(RabbitMqOptions.Password)] ?? options.Password;

            if (int.TryParse(section[nameof(RabbitMqOptions.Port)], out var port))
            {
                options.Port = port;
            }
        });

        services.AddMassTransit(registration =>
        {
            registration.SetKebabCaseEndpointNameFormatter();
            configureConsumers?.Invoke(registration);

            registration.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

                cfg.Host(options.Host, (ushort)options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.Username);
                    host.Password(options.Password);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        return services;
    }
}
