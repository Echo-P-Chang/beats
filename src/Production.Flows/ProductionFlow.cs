namespace Beats.Production.Flows;

public sealed class ProductionFlow : IProductionFlow
{
    private readonly IReadOnlyDictionary<string, AgentFlowDefinition> _flows;

    public ProductionFlow(IReadOnlyList<AgentFlowDefinition> flows)
    {
        _flows = flows.ToDictionary(
            flow => flow.AgentRole,
            StringComparer.OrdinalIgnoreCase);
    }

    public AgentFlowDefinition GetAgentFlow(string agentRole)
    {
        if (_flows.TryGetValue(agentRole, out var flow))
        {
            return flow;
        }

        throw new InvalidOperationException($"No production flow is registered for agent role '{agentRole}'.");
    }

    public IReadOnlyList<FlowSubscription> GetSubscriptions(string agentRole)
    {
        return GetAgentFlow(agentRole).Subscriptions;
    }

    public IReadOnlyList<FlowPublication> GetPublications(string agentRole)
    {
        return GetAgentFlow(agentRole).Publications;
    }

    public FlowPublication GetRequiredPublication<TPayload>(
        string agentRole,
        string? afterEventType = null)
    {
        var payloadType = typeof(TPayload);
        var publications = GetPublications(agentRole);

        var matches = publications
            .Where(publication =>
                publication.PayloadType == payloadType &&
                string.Equals(publication.AfterEventType, afterEventType, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No publication is registered for agent role '{agentRole}', payload '{payloadType.Name}', after event '{afterEventType ?? "<start>"}'."),
            _ => throw new InvalidOperationException(
                $"Multiple publications are registered for agent role '{agentRole}', payload '{payloadType.Name}', after event '{afterEventType ?? "<start>"}'.")
        };
    }
}
