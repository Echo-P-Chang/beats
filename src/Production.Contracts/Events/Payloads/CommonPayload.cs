using Beats.Production.Contracts.Media;

namespace Beats.Production.Contracts.Events.Payloads;

public sealed record CommonPayload(
    string EventType,
    string Stage,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<ArtifactReference> Artifacts,
    IReadOnlyDictionary<string, object?> Data)
{
    public static CommonPayload Create(
        string eventType,
        string stage,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<ArtifactReference>? artifacts = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        return new CommonPayload(
            eventType,
            stage,
            attributes ?? new Dictionary<string, string>(),
            artifacts ?? [],
            data ?? new Dictionary<string, object?>());
    }
}
