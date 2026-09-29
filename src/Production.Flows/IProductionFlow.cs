namespace Beats.Production.Flows;

public interface IProductionFlow
{
    AgentFlowDefinition GetAgentFlow(string agentRole);

    IReadOnlyList<FlowSubscription> GetSubscriptions(string agentRole);

    IReadOnlyList<FlowPublication> GetPublications(string agentRole);

    bool IsSubscribedTo(
        string agentRole,
        string eventType);

    FlowPublication GetRequiredPublication<TPayload>(
        string agentRole,
        string? afterEventType = null);
}
