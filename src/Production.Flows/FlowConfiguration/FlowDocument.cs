namespace Beats.Production.Flows.FlowConfiguration;

internal sealed class FlowDocument
{
    public List<AgentFlowDocument> AgentFlows { get; set; } = [];
}
