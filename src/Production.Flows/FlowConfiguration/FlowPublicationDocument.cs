namespace Beats.Production.Flows.FlowConfiguration;

internal sealed class FlowPublicationDocument
{
    public string EventType { get; set; } = string.Empty;

    public string PayloadType { get; set; } = string.Empty;

    public string? AfterEventType { get; set; }
}
