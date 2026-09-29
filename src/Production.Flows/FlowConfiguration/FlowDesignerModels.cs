namespace Beats.Production.Flows.FlowConfiguration;

public sealed record FlowDesignerModel(
    IReadOnlyList<FlowDesignerAgent> AgentFlows,
    IReadOnlyList<FlowDesignerEdge> Edges,
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<FlowDesignerEventProfile> EventProfiles,
    IReadOnlyList<string> PayloadTypes,
    IReadOnlyList<string> Warnings);

public sealed record FlowDesignerEventProfile(
    string EventType,
    string Stage,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<string> Data);

public sealed record FlowDesignerAgent(
    string AgentRole,
    IReadOnlyList<FlowDesignerSubscription> Subscriptions,
    IReadOnlyList<FlowDesignerPublication> Publications);

public sealed record FlowDesignerSubscription(
    string EventType,
    string PayloadType);

public sealed record FlowDesignerPublication(
    string EventType,
    string PayloadType,
    string? AfterEventType);

public sealed record FlowDesignerEdge(
    string SourceAgentRole,
    string? TargetAgentRole,
    string EventType,
    string PayloadType,
    string? AfterEventType);

public sealed record ProductionFlowValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
