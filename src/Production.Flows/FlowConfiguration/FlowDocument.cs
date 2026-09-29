namespace Beats.Production.Flows.FlowConfiguration;

internal sealed class FlowDocument
{
    public List<AgentFlowDocument> AgentFlows { get; set; } = [];

    public List<FlowEventProfileDocument> EventProfiles { get; set; } = [];
}

internal sealed class FlowEventProfileDocument
{
    public string EventType { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public List<string> Attributes { get; set; } = [];

    public List<string> Inputs { get; set; } = [];

    public List<string> Outputs { get; set; } = [];

    public List<string> Artifacts { get; set; } = [];

    public List<string> Data { get; set; } = [];
}
