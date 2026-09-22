using System.Reflection;
using Beats.Production.Contracts.Events;
using Beats.Production.Flows.FlowConfiguration;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beats.Production.Flows.DependencyInjection;

public static class ProductionFlowServiceCollectionExtensions
{
    public static IServiceCollection AddProductionFlows(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var flows = ProductionFlowDefinitionSource.Load(configuration);
        services.AddSingleton<IProductionFlow>(_ => new ProductionFlow(flows));

        return services;
    }

    public static void AddFlowConsumersForAgent(
        this IBusRegistrationConfigurator registration,
        IConfiguration configuration,
        string agentRole,
        Assembly agentAssembly)
    {
        var flow = ProductionFlowDefinitionSource.Load(configuration).SingleOrDefault(
            definition => string.Equals(definition.AgentRole, agentRole, StringComparison.OrdinalIgnoreCase));

        if (flow is null)
        {
            throw new InvalidOperationException($"No production flow is registered for agent role '{agentRole}'.");
        }

        foreach (var subscription in flow.Subscriptions)
        {
            var consumerType = FindConsumerType(agentAssembly, subscription);
            registration.AddConsumer(consumerType);
        }
    }

    private static Type FindConsumerType(Assembly agentAssembly, FlowSubscription subscription)
    {
        var consumerTypes = agentAssembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => ConsumesPayload(type, subscription.PayloadType))
            .ToArray();

        return consumerTypes.Length switch
        {
            1 => consumerTypes[0],
            0 => throw new InvalidOperationException(
                $"No consumer for event '{subscription.EventType}' and payload '{subscription.PayloadType.Name}' was found in assembly '{agentAssembly.GetName().Name}'."),
            _ => throw new InvalidOperationException(
                $"Multiple consumers for event '{subscription.EventType}' and payload '{subscription.PayloadType.Name}' were found in assembly '{agentAssembly.GetName().Name}'.")
        };
    }

    private static bool ConsumesPayload(Type consumerType, Type payloadType)
    {
        var envelopeType = typeof(EventEnvelope<>).MakeGenericType(payloadType);
        var consumerInterfaceType = typeof(IConsumer<>).MakeGenericType(envelopeType);

        return consumerInterfaceType.IsAssignableFrom(consumerType);
    }
}
