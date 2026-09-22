using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Beats.Production.Flows.FlowConfiguration;

public static class ProductionFlowDefinitionSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<AgentFlowDefinition> Load(IConfiguration configuration)
    {
        var options = new ProductionFlowOptions();
        var section = configuration.GetSection(ProductionFlowOptions.SectionName);
        options.ConfigPath = section[nameof(ProductionFlowOptions.ConfigPath)] ?? options.ConfigPath;

        var path = ResolvePath(options.ConfigPath);
        var document = JsonSerializer.Deserialize<FlowDocument>(
            File.ReadAllText(path),
            JsonOptions);

        if (document is null)
        {
            throw new InvalidOperationException($"Flow config '{path}' could not be parsed.");
        }

        var flows = document.AgentFlows
            .Select(ToAgentFlowDefinition)
            .ToArray();

        Validate(flows, path);

        return flows;
    }

    private static AgentFlowDefinition ToAgentFlowDefinition(AgentFlowDocument document)
    {
        return new AgentFlowDefinition(
            RequireValue(document.AgentRole, "agentRole"),
            document.Subscriptions.Select(subscription => new FlowSubscription(
                RequireValue(subscription.EventType, "subscription.eventType"),
                FlowPayloadTypeResolver.Resolve(RequireValue(subscription.PayloadType, "subscription.payloadType")))).ToArray(),
            document.Publications.Select(publication => new FlowPublication(
                RequireValue(publication.EventType, "publication.eventType"),
                FlowPayloadTypeResolver.Resolve(RequireValue(publication.PayloadType, "publication.payloadType")),
                string.IsNullOrWhiteSpace(publication.AfterEventType) ? null : publication.AfterEventType)).ToArray());
    }

    private static void Validate(IReadOnlyList<AgentFlowDefinition> flows, string path)
    {
        if (flows.Count == 0)
        {
            throw new InvalidOperationException($"Flow config '{path}' does not contain any agent flows.");
        }

        var duplicateAgentRoles = flows
            .GroupBy(flow => flow.AgentRole, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateAgentRoles.Length > 0)
        {
            throw new InvalidOperationException(
                $"Flow config '{path}' contains duplicate agent roles: {string.Join(", ", duplicateAgentRoles)}.");
        }
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Flow config field '{name}' is required.");
        }

        return value;
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
