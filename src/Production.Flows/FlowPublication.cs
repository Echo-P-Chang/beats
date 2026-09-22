namespace Beats.Production.Flows;

public sealed record FlowPublication(
    string EventType,
    Type PayloadType,
    string? AfterEventType = null);
