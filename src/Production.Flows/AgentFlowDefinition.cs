namespace Beats.Production.Flows;

public sealed record AgentFlowDefinition(
    string AgentRole,
    IReadOnlyList<FlowSubscription> Subscriptions,
    IReadOnlyList<FlowPublication> Publications);
