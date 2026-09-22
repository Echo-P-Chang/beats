namespace Beats.Production.Flows;

public sealed record FlowSubscription(
    string EventType,
    Type PayloadType);
