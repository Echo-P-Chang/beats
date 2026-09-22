namespace Beats.Production.Flows;

public sealed class ProductionFlowOptions
{
    public const string SectionName = "ProductionFlows";

    public string ConfigPath { get; set; } = "flows/production-flow.json";
}
