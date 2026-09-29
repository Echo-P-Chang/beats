using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Beats.Production.Flows.FlowConfiguration;

public static class ProductionFlowDesignerSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static FlowDesignerModel Load(IConfiguration configuration)
    {
        var path = ResolvePath(
            configuration[$"{ProductionFlowOptions.SectionName}:{nameof(ProductionFlowOptions.ConfigPath)}"]
                ?? "flows/production-flow.json");

        return CreateDesignerModel(File.ReadAllText(path));
    }

    public static FlowDesignerModel CreateDesignerModel(string json)
    {
        var validation = ValidateJson(json);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Flow config is invalid: {string.Join("; ", validation.Errors)}");
        }

        var document = Deserialize(json)!;
        var agentFlows = document.AgentFlows
            .Select(agent => new FlowDesignerAgent(
                agent.AgentRole,
                agent.Subscriptions
                    .Select(subscription => new FlowDesignerSubscription(
                        subscription.EventType,
                        subscription.PayloadType))
                    .ToArray(),
                agent.Publications
                    .Select(publication => new FlowDesignerPublication(
                        publication.EventType,
                        publication.PayloadType,
                        publication.AfterEventType))
                    .ToArray()))
            .ToArray();

        var edges = document.AgentFlows
            .SelectMany(publisher => publisher.Publications.SelectMany(publication =>
            {
                var subscribers = document.AgentFlows
                    .Where(subscriber => subscriber.Subscriptions.Any(subscription =>
                        string.Equals(subscription.EventType, publication.EventType, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (subscribers.Length == 0)
                {
                    return
                    [
                        new FlowDesignerEdge(
                            publisher.AgentRole,
                            null,
                            publication.EventType,
                            publication.PayloadType,
                            publication.AfterEventType)
                    ];
                }

                return subscribers.Select(subscriber => new FlowDesignerEdge(
                    publisher.AgentRole,
                    subscriber.AgentRole,
                    publication.EventType,
                    publication.PayloadType,
                    publication.AfterEventType));
            }))
            .ToArray();

        var eventTypes = document.AgentFlows
            .SelectMany(agent => agent.Subscriptions.Select(subscription => subscription.EventType)
                .Concat(agent.Publications.Select(publication => publication.EventType)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var warnings = edges
            .Where(edge => edge.TargetAgentRole is null)
            .Select(edge => $"Event '{edge.EventType}' is published by '{edge.SourceAgentRole}' but has no subscriber.")
            .ToArray();

        return new FlowDesignerModel(
            agentFlows,
            edges,
            eventTypes,
            BuildEventProfiles(document, eventTypes),
            FlowPayloadTypeResolver.GetKnownPayloadTypeNames(),
            warnings);
    }

    public static ProductionFlowValidationResult ValidateJson(string json)
    {
        var errors = new List<string>();
        FlowDocument? document;

        try
        {
            document = Deserialize(json);
        }
        catch (JsonException exception)
        {
            return new ProductionFlowValidationResult(
                false,
                [$"JSON could not be parsed: {exception.Message}"]);
        }

        if (document is null)
        {
            return new ProductionFlowValidationResult(false, ["Flow config could not be parsed."]);
        }

        if (document.AgentFlows.Count == 0)
        {
            errors.Add("agentFlows must contain at least one agent.");
        }

        ValidateAgents(document, errors);
        ValidateEventPayloadConsistency(document, errors);
        ValidateEventProfiles(document, errors);
        ValidateAfterEventTypes(document, errors);
        ValidateEventGraphCycles(document, errors);

        return new ProductionFlowValidationResult(errors.Count == 0, errors);
    }

    private static FlowDocument? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<FlowDocument>(json, JsonOptions);
    }

    private static void ValidateAgents(FlowDocument document, List<string> errors)
    {
        var seenAgentRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var agentIndex = 0; agentIndex < document.AgentFlows.Count; agentIndex++)
        {
            var agent = document.AgentFlows[agentIndex];
            var agentPath = $"agentFlows[{agentIndex}]";

            if (string.IsNullOrWhiteSpace(agent.AgentRole))
            {
                errors.Add($"{agentPath}.agentRole is required.");
            }
            else if (!seenAgentRoles.Add(agent.AgentRole))
            {
                errors.Add($"{agentPath}.agentRole '{agent.AgentRole}' is duplicated.");
            }

            ValidateSubscriptions(agent, agentPath, errors);
            ValidatePublications(agent, agentPath, errors);
        }
    }

    private static void ValidateSubscriptions(
        AgentFlowDocument agent,
        string agentPath,
        List<string> errors)
    {
        var seenSubscriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var subscriptionIndex = 0; subscriptionIndex < agent.Subscriptions.Count; subscriptionIndex++)
        {
            var subscription = agent.Subscriptions[subscriptionIndex];
            var subscriptionPath = $"{agentPath}.subscriptions[{subscriptionIndex}]";

            ValidateRequiredEventPayload(
                subscription.EventType,
                subscription.PayloadType,
                subscriptionPath,
                errors);

            if (!string.IsNullOrWhiteSpace(subscription.EventType) &&
                !seenSubscriptions.Add(subscription.EventType))
            {
                errors.Add($"{subscriptionPath}.eventType '{subscription.EventType}' is subscribed more than once by '{agent.AgentRole}'.");
            }
        }
    }

    private static void ValidatePublications(
        AgentFlowDocument agent,
        string agentPath,
        List<string> errors)
    {
        var seenPublications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var publicationIndex = 0; publicationIndex < agent.Publications.Count; publicationIndex++)
        {
            var publication = agent.Publications[publicationIndex];
            var publicationPath = $"{agentPath}.publications[{publicationIndex}]";

            ValidateRequiredEventPayload(
                publication.EventType,
                publication.PayloadType,
                publicationPath,
                errors);

            if (!string.IsNullOrWhiteSpace(publication.EventType) &&
                !seenPublications.Add(publication.EventType))
            {
                errors.Add($"{publicationPath}.eventType '{publication.EventType}' is published more than once by '{agent.AgentRole}'.");
            }
        }
    }

    private static void ValidateRequiredEventPayload(
        string eventType,
        string payloadType,
        string path,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            errors.Add($"{path}.eventType is required.");
        }

        if (string.IsNullOrWhiteSpace(payloadType))
        {
            errors.Add($"{path}.payloadType is required.");
            return;
        }

        try
        {
            FlowPayloadTypeResolver.Resolve(payloadType);
        }
        catch (InvalidOperationException exception)
        {
            errors.Add($"{path}.payloadType is invalid. {exception.Message}");
        }
    }

    private static void ValidateEventPayloadConsistency(FlowDocument document, List<string> errors)
    {
        var eventPayloadTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.AgentFlows.SelectMany(agent =>
            agent.Subscriptions.Select(subscription => new
            {
                AgentRole = agent.AgentRole,
                Kind = "subscription",
                subscription.EventType,
                subscription.PayloadType
            })
            .Concat(agent.Publications.Select(publication => new
            {
                AgentRole = agent.AgentRole,
                Kind = "publication",
                publication.EventType,
                publication.PayloadType
            }))))
        {
            if (string.IsNullOrWhiteSpace(entry.EventType) ||
                string.IsNullOrWhiteSpace(entry.PayloadType))
            {
                continue;
            }

            if (eventPayloadTypes.TryGetValue(entry.EventType, out var existingPayloadType) &&
                !string.Equals(existingPayloadType, entry.PayloadType, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Event '{entry.EventType}' uses payload '{existingPayloadType}' elsewhere, but '{entry.AgentRole}' {entry.Kind} uses '{entry.PayloadType}'.");
                continue;
            }

            eventPayloadTypes[entry.EventType] = entry.PayloadType;
        }
    }

    private static void ValidateEventProfiles(FlowDocument document, List<string> errors)
    {
        var knownEvents = document.AgentFlows
            .SelectMany(agent => agent.Subscriptions.Select(subscription => subscription.EventType)
                .Concat(agent.Publications.Select(publication => publication.EventType)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var profileIndex = 0; profileIndex < document.EventProfiles.Count; profileIndex++)
        {
            var profile = document.EventProfiles[profileIndex];
            var profilePath = $"eventProfiles[{profileIndex}]";

            if (string.IsNullOrWhiteSpace(profile.EventType))
            {
                errors.Add($"{profilePath}.eventType is required.");
                continue;
            }

            if (!seenProfiles.Add(profile.EventType))
            {
                errors.Add($"{profilePath}.eventType '{profile.EventType}' is duplicated.");
            }

            if (!knownEvents.Contains(profile.EventType))
            {
                errors.Add($"{profilePath}.eventType '{profile.EventType}' does not exist in agentFlows.");
            }
        }
    }

    private static FlowDesignerEventProfile[] BuildEventProfiles(
        FlowDocument document,
        IReadOnlyCollection<string> eventTypes)
    {
        var configuredProfiles = document.EventProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.EventType))
            .GroupBy(profile => profile.EventType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return eventTypes
            .Select(eventType =>
            {
                configuredProfiles.TryGetValue(eventType, out var configuredProfile);
                return CreateEventProfile(document, eventType, configuredProfile);
            })
            .ToArray();
    }

    private static FlowDesignerEventProfile CreateEventProfile(
        FlowDocument document,
        string eventType,
        FlowEventProfileDocument? configuredProfile)
    {
        var defaults = GetDefaultEventProfile(eventType);

        return new FlowDesignerEventProfile(
            eventType,
            SelectText(configuredProfile?.Stage, defaults.Stage, InferPublisherStage(document, eventType)),
            SelectProfileAttributes(configuredProfile, defaults.Attributes),
            SelectList(configuredProfile?.Artifacts, defaults.Artifacts),
            SelectList(configuredProfile?.Data, defaults.Data));
    }

    private static FlowDesignerEventProfile GetDefaultEventProfile(string eventType)
    {
        return eventType switch
        {
            "ProductionRequested" => new FlowDesignerEventProfile(
                eventType,
                "production-api",
                ["prompt", "style", "targetWordCount", "durationSeconds"],
                [],
                []),
            "StoryCreated" => new FlowDesignerEventProfile(
                eventType,
                "storyteller-agent",
                ["prompt", "style", "targetWordCount", "durationSeconds", "storyUri", "scenesUri"],
                ["manuscript"],
                ["model", "finishReason", "validation"]),
            "SceneImagesCreated" => new FlowDesignerEventProfile(
                eventType,
                "illustrator-agent",
                ["prompt", "style", "targetWordCount", "durationSeconds", "storyUri", "scenesUri", "manifestUri", "imageCount"],
                ["sceneImages"],
                ["comfyUiModel", "maxScenes"]),
            "SceneAnimationsCreated" => new FlowDesignerEventProfile(
                eventType,
                "animator-agent",
                ["prompt", "style", "targetWordCount", "durationSeconds", "manifestUri", "imageCount", "animationPlanUri", "animationCount"],
                ["animationPlan"],
                []),
            "FinalVideoCreated" => new FlowDesignerEventProfile(
                eventType,
                "editor-agent",
                ["prompt", "style", "targetWordCount", "durationSeconds", "animationPlanUri", "animationCount", "videoUri", "voiceOverUri"],
                ["finalVideoManifest"],
                []),
            "ReviewPassed" => new FlowDesignerEventProfile(
                eventType,
                "reviewer-agent",
                ["prompt", "style", "targetWordCount", "durationSeconds", "videoUri", "voiceOverUri", "reviewReportUri", "reviewPassed"],
                ["reviewReport"],
                ["findings"]),
            _ => new FlowDesignerEventProfile(eventType, string.Empty, [], [], [])
        };
    }

    private static string InferPublisherStage(FlowDocument document, string eventType)
    {
        return document.AgentFlows
            .FirstOrDefault(agent => agent.Publications.Any(publication =>
                string.Equals(publication.EventType, eventType, StringComparison.OrdinalIgnoreCase)))
            ?.AgentRole ?? string.Empty;
    }

    private static string SelectText(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static IReadOnlyList<string> SelectList(List<string>? configuredValues, IReadOnlyList<string> defaultValues)
    {
        if (configuredValues is null)
        {
            return defaultValues;
        }

        return configuredValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
    }

    private static IReadOnlyList<string> SelectProfileAttributes(
        FlowEventProfileDocument? configuredProfile,
        IReadOnlyList<string> defaultValues)
    {
        if (configuredProfile is null)
        {
            return defaultValues;
        }

        var attributes = SelectList(configuredProfile.Attributes, []);

        if (attributes.Count > 0)
        {
            return attributes;
        }

        var legacyValues = SelectList(configuredProfile.Inputs, [])
            .Concat(SelectList(configuredProfile.Outputs, []))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return legacyValues.Length > 0 ? legacyValues : defaultValues;
    }

    private static void ValidateAfterEventTypes(FlowDocument document, List<string> errors)
    {
        var publishedEvents = document.AgentFlows
            .SelectMany(agent => agent.Publications)
            .Select(publication => publication.EventType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var publication in document.AgentFlows.SelectMany(agent => agent.Publications))
        {
            if (string.IsNullOrWhiteSpace(publication.AfterEventType))
            {
                continue;
            }

            if (!publishedEvents.Contains(publication.AfterEventType))
            {
                errors.Add(
                    $"Publication '{publication.EventType}' has afterEventType '{publication.AfterEventType}', but that event is not published by any agent.");
            }
        }
    }

    private static void ValidateEventGraphCycles(FlowDocument document, List<string> errors)
    {
        var graph = document.AgentFlows
            .SelectMany(agent => agent.Publications)
            .Where(publication =>
                !string.IsNullOrWhiteSpace(publication.AfterEventType) &&
                !string.IsNullOrWhiteSpace(publication.EventType))
            .GroupBy(publication => publication.AfterEventType!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(publication => publication.EventType).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var eventType in graph.Keys)
        {
            if (HasCycle(eventType, graph, visiting, visited))
            {
                errors.Add($"Event flow contains a cycle involving '{eventType}'.");
                return;
            }
        }
    }

    private static bool HasCycle(
        string eventType,
        IReadOnlyDictionary<string, string[]> graph,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(eventType))
        {
            return false;
        }

        if (!visiting.Add(eventType))
        {
            return true;
        }

        if (graph.TryGetValue(eventType, out var nextEvents))
        {
            foreach (var nextEvent in nextEvents)
            {
                if (HasCycle(nextEvent, graph, visiting, visited))
                {
                    return true;
                }
            }
        }

        visiting.Remove(eventType);
        visited.Add(eventType);

        return false;
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        foreach (var root in CandidateRoots())
        {
            var candidate = Path.GetFullPath(Path.Combine(root, configuredPath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Flow config file '{configuredPath}' was not found.");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var roots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }
    }
}
