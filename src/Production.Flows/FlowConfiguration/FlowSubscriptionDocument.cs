namespace Beats.Production.Flows.FlowConfiguration;

internal sealed class FlowSubscriptionDocument
{
    public string EventType { get; set; } = string.Empty;

    public string PayloadType { get; set; } = string.Empty;
}
