using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Beats.Production.Middleware.Ai;

public sealed class ComfyUiImageGenerationClient(
    HttpClient httpClient,
    IOptions<ComfyUiOptions> options) : IImageGenerationClient
{
    private readonly ComfyUiOptions _options = options.Value;
    private readonly string _clientId = $"beats-{Guid.NewGuid():N}";

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        var startedAt = Stopwatch.StartNew();
        var workflow = BuildFluxWorkflow(request);
        var queueResponse = await QueuePromptAsync(workflow, cancellationToken);
        var image = await WaitForFirstImageAsync(queueResponse.PromptId, startedAt, cancellationToken);
        var content = await DownloadImageAsync(image, cancellationToken);

        return new ImageGenerationResponse(
            content,
            "image/png",
            image.FileName,
            image.Subfolder,
            image.Type,
            queueResponse.PromptId,
            startedAt.Elapsed);
    }

    private async Task<QueuePromptResponse> QueuePromptAsync(
        JsonObject workflow,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["prompt"] = workflow,
            ["client_id"] = _clientId
        };

        using var response = await httpClient.PostAsJsonAsync(
            "/prompt",
            body,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<QueuePromptResponse>(
            cancellationToken: cancellationToken);

        return payload ?? throw new InvalidOperationException("ComfyUI returned an empty queue response.");
    }

    private async Task<ComfyImageReference> WaitForFirstImageAsync(
        string promptId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        while (stopwatch.Elapsed < timeout)
        {
            using var response = await httpClient.GetAsync($"/history/{Uri.EscapeDataString(promptId)}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var history = await response.Content.ReadFromJsonAsync<JsonObject>(
                cancellationToken: cancellationToken);

            if (TryGetError(history, promptId, out var error))
            {
                throw new InvalidOperationException($"ComfyUI generation failed: {error}");
            }

            if (TryGetFirstImage(history, promptId, out var image))
            {
                return image;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new TimeoutException($"ComfyUI did not finish prompt {promptId} within {timeout.TotalSeconds:0} seconds.");
    }

    private async Task<byte[]> DownloadImageAsync(
        ComfyImageReference image,
        CancellationToken cancellationToken)
    {
        var query = $"/view?filename={Uri.EscapeDataString(image.FileName)}" +
            $"&subfolder={Uri.EscapeDataString(image.Subfolder ?? string.Empty)}" +
            $"&type={Uri.EscapeDataString(image.Type)}";

        return await httpClient.GetByteArrayAsync(query, cancellationToken);
    }

    private JsonObject BuildFluxWorkflow(ImageGenerationRequest request)
    {
        var positiveText = request.Prompt.Trim();
        var negativeText = request.NegativePrompt?.Trim() ?? string.Empty;
        var width = request.Width ?? _options.Width;
        var height = request.Height ?? _options.Height;
        var steps = request.Steps ?? _options.Steps;
        var guidance = request.Guidance ?? _options.Guidance;
        var seed = request.Seed ?? CreateSeed();
        var prefix = string.IsNullOrWhiteSpace(request.FileNamePrefix)
            ? "beats-flux"
            : SanitizeFileNamePrefix(request.FileNamePrefix);

        return new JsonObject
        {
            ["1"] = Node("UnetLoaderGGUF", new JsonObject
            {
                ["unet_name"] = _options.Model
            }),
            ["2"] = Node("DualCLIPLoaderGGUF", new JsonObject
            {
                ["clip_name1"] = _options.ClipName1,
                ["clip_name2"] = _options.ClipName2,
                ["type"] = "flux"
            }),
            ["3"] = Node("VAELoader", new JsonObject
            {
                ["vae_name"] = _options.VaeName
            }),
            ["4"] = Node("CLIPTextEncode", new JsonObject
            {
                ["text"] = positiveText,
                ["clip"] = Link("2")
            }),
            ["5"] = Node("FluxGuidance", new JsonObject
            {
                ["conditioning"] = Link("4"),
                ["guidance"] = guidance
            }),
            ["6"] = Node("CLIPTextEncode", new JsonObject
            {
                ["text"] = negativeText,
                ["clip"] = Link("2")
            }),
            ["7"] = Node("EmptyLatentImage", new JsonObject
            {
                ["width"] = width,
                ["height"] = height,
                ["batch_size"] = 1
            }),
            ["8"] = Node("KSampler", new JsonObject
            {
                ["model"] = Link("1"),
                ["seed"] = seed,
                ["steps"] = steps,
                ["cfg"] = _options.Cfg,
                ["sampler_name"] = _options.SamplerName,
                ["scheduler"] = _options.Scheduler,
                ["positive"] = Link("5"),
                ["negative"] = Link("6"),
                ["latent_image"] = Link("7"),
                ["denoise"] = 1.0
            }),
            ["9"] = Node("VAEDecode", new JsonObject
            {
                ["samples"] = Link("8"),
                ["vae"] = Link("3")
            }),
            ["10"] = Node("SaveImage", new JsonObject
            {
                ["images"] = Link("9"),
                ["filename_prefix"] = prefix
            })
        };
    }

    private static JsonObject Node(string classType, JsonObject inputs)
    {
        return new JsonObject
        {
            ["class_type"] = classType,
            ["inputs"] = inputs
        };
    }

    private static JsonArray Link(string nodeId, int output = 0)
    {
        return new JsonArray(JsonValue.Create(nodeId), JsonValue.Create(output));
    }

    private static bool TryGetFirstImage(
        JsonObject? history,
        string promptId,
        out ComfyImageReference image)
    {
        image = default!;

        if (history?[promptId]?["outputs"] is not JsonObject outputs)
        {
            return false;
        }

        foreach (var output in outputs)
        {
            if (output.Value?["images"] is not JsonArray images)
            {
                continue;
            }

            foreach (var item in images)
            {
                var fileName = item?["filename"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                image = new ComfyImageReference(
                    fileName,
                    item?["subfolder"]?.GetValue<string>(),
                    item?["type"]?.GetValue<string>() ?? "output");

                return true;
            }
        }

        return false;
    }

    private static bool TryGetError(
        JsonObject? history,
        string promptId,
        out string error)
    {
        error = string.Empty;
        var status = history?[promptId]?["status"];
        var statusText = status?["status_str"]?.GetValue<string>();

        if (!string.Equals(statusText, "error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        error = status?["messages"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ??
            "unknown ComfyUI error";

        return true;
    }

    private static ulong CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static string SanitizeFileNamePrefix(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());

        return sanitized.Length == 0 ? "beats-flux" : sanitized;
    }

    private sealed record QueuePromptResponse(
        [property: JsonPropertyName("prompt_id")] string PromptId);

    private sealed record ComfyImageReference(
        string FileName,
        string? Subfolder,
        string Type);
}
