namespace Beats.Production.Flows.FlowConfiguration;

internal sealed class AgentFlowDocument
{
    public string AgentRole { get; set; } = string.Empty;

    public List<FlowSubscriptionDocument> Subscriptions { get; set; } = [];

    public List<FlowPublicationDocument> Publications { get; set; } = [];
}
