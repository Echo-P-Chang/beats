using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Beats.Production.Middleware.Ai;

public sealed class OllamaTextGenerationClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options) : ITextGenerationClient
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<TextGenerationResponse> GenerateAsync(
        TextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        var ollamaRequest = new OllamaGenerateRequest(
            request.Model ?? _options.Model,
            request.Prompt,
            string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim(),
            false,
            new OllamaGenerateOptions(
                request.Temperature ?? _options.Temperature,
                request.NumPredict ?? _options.NumPredict));

        using var response = await httpClient.PostAsJsonAsync(
            "/api/generate",
            ollamaRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken: cancellationToken);

        if (payload is null)
        {
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        var text = CleanResponseText(payload.Response);

        return new TextGenerationResponse(
            text,
            payload.Model,
            DurationFromNanoseconds(payload.TotalDuration),
            payload.DoneReason);
    }

    private static TimeSpan? DurationFromNanoseconds(long? nanoseconds)
    {
        if (nanoseconds is null)
        {
            return null;
        }

        return TimeSpan.FromTicks(nanoseconds.Value / 100);
    }

    private static string CleanResponseText(string text)
    {
        var trimmed = text.Trim();
        var thoughtStart = IndexOfAny(
            trimmed,
            StringComparison.OrdinalIgnoreCase,
            "<|channel|>thought",
            "<|channel>thought");

        if (thoughtStart < 0)
        {
            return trimmed;
        }

        var finalStart = IndexOfAny(
            trimmed,
            StringComparison.OrdinalIgnoreCase,
            "<|channel|>final",
            "<|channel>final");

        if (finalStart >= 0)
        {
            var finalMarkerEnd = trimmed.IndexOf('>', finalStart);
            return finalMarkerEnd >= 0
                ? trimmed[(finalMarkerEnd + 1)..].Trim()
                : trimmed[(finalStart + "<|channel|>final".Length)..].Trim();
        }

        var channelEnd = trimmed.LastIndexOf("<channel|>", StringComparison.OrdinalIgnoreCase);
        if (channelEnd >= 0 && channelEnd > thoughtStart)
        {
            return trimmed[(channelEnd + "<channel|>".Length)..].Trim();
        }

        return trimmed[..thoughtStart].Trim();
    }

    private static int IndexOfAny(
        string text,
        StringComparison comparison,
        params string[] values)
    {
        var indexes = values
            .Select(value => text.IndexOf(value, comparison))
            .Where(index => index >= 0)
            .ToArray();

        return indexes.Length == 0 ? -1 : indexes.Min();
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaGenerateOptions Options);

    private sealed record OllamaGenerateOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("response")] string Response,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("total_duration")] long? TotalDuration);
}
