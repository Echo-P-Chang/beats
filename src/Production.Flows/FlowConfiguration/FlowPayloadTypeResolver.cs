using Beats.Production.Contracts.Events.Payloads;

namespace Beats.Production.Flows.FlowConfiguration;

internal static class FlowPayloadTypeResolver
{
    private static readonly IReadOnlyDictionary<string, Type> PayloadTypes =
        new[]
        {
            typeof(ProductionRequestedPayload),
            typeof(StoryCreatedPayload),
            typeof(SceneImagesCreatedPayload),
            typeof(SceneAnimationsCreatedPayload),
            typeof(FinalVideoCreatedPayload),
            typeof(ReviewCompletedPayload)
        }
        .SelectMany(type => new[]
        {
            new KeyValuePair<string, Type>(type.Name, type),
            new KeyValuePair<string, Type>(type.FullName ?? type.Name, type)
        })
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public static Type Resolve(string payloadType)
    {
        if (PayloadTypes.TryGetValue(payloadType, out var type))
        {
            return type;
        }

        throw new InvalidOperationException(
            $"Unknown flow payload type '{payloadType}'. Use one of: {string.Join(", ", PayloadTypes.Keys.Order())}.");
    }
}
