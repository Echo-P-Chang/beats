using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Beats.Production.Contracts.Media;
using Beats.Production.Middleware.Artifacts;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Beats.Agents.Animator;

public sealed class AzureSoraVideoAnimatorAdapter(
    HttpClient httpClient,
    IArtifactStore artifactStore,
    IOptions<AzureSoraVideoOptions> options,
    ILogger<AzureSoraVideoAnimatorAdapter> logger) : IAnimatorAdapter
{
    private readonly AzureSoraVideoOptions _options = options.Value;
    private readonly TokenCredential _credential = new DefaultAzureCredential();

    public async Task<AnimationPlanResult> CreateAnimationPlanAsync(
        AnimationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ImageArtifacts.Count == 0)
        {
            throw new InvalidOperationException("Azure Sora animator requires at least one image artifact.");
        }

        var sceneVideos = new List<GeneratedSceneVideo>();
        var generatedArtifacts = new List<GeneratedAnimationArtifact>();

        for (var index = 0; index < request.ImageArtifacts.Count; index++)
        {
            var clips = BuildClipDurations(request, index);
            var sceneVideosForImage = await CreateSceneVideosAsync(
                request,
                request.ImageArtifacts[index],
                index,
                clips,
                cancellationToken);

            foreach (var sceneVideo in sceneVideosForImage)
            {
                sceneVideos.Add(sceneVideo);
                generatedArtifacts.Add(new GeneratedAnimationArtifact(
                    sceneVideo.OutputFileName,
                    "video/mp4",
                    $"Sora image-to-video output for scene {index + 1:000}, clip {sceneVideo.SegmentNumber:000}/{sceneVideo.SegmentCount:000}, {sceneVideo.ClipSeconds}s. VideoId={sceneVideo.VideoId}. AzureBlob={sceneVideo.OutputBlobUri}",
                    sceneVideo.VideoBytes));
            }
        }

        var content = BuildArtifactContent(request, sceneVideos);

        return new AnimationPlanResult(
            content,
            $"Generated {generatedArtifacts.Count} Sora image-to-video artifact(s).",
            generatedArtifacts.Count,
            generatedArtifacts);
    }

    private async Task<IReadOnlyList<GeneratedSceneVideo>> CreateSceneVideosAsync(
        AnimationPlanRequest request,
        ArtifactReference image,
        int sceneIndex,
        IReadOnlyList<int> clipDurations,
        CancellationToken cancellationToken)
    {
        var imageBytes = await ReadArtifactBytesAsync(image, cancellationToken);
        var normalizedImage = await NormalizeInputImageAsync(imageBytes, cancellationToken);
        var imageFileName = BuildInputFileName(image, normalizedImage.MediaType);

        var inputBlobUri = await UploadInputImageAsync(
            request,
            imageFileName,
            normalizedImage.MediaType,
            normalizedImage.Content,
            cancellationToken);

        logger.LogInformation(
            "Calling Azure Sora video generation. ProductionId={ProductionId}, SceneIndex={SceneIndex}, InputBlob={InputBlobUri}",
            request.ProductionId,
            sceneIndex + 1,
            inputBlobUri);

        var sceneVideos = new List<GeneratedSceneVideo>();

        for (var segmentIndex = 0; segmentIndex < clipDurations.Count; segmentIndex++)
        {
            var clipSeconds = clipDurations[segmentIndex];
            var prompt = BuildVideoPrompt(request, image, sceneIndex, segmentIndex, clipDurations.Count, clipSeconds);
            var videoId = await CreateVideoAsync(
                prompt,
                imageFileName,
                normalizedImage.MediaType,
                normalizedImage.Content,
                clipSeconds.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
            var videoBytes = await WaitForVideoAndDownloadAsync(videoId, cancellationToken);

            var outputFileName = BuildOutputFileName(imageFileName, sceneIndex, segmentIndex, clipDurations.Count);
            var outputBlobUri = await UploadOutputVideoAsync(
                request,
                outputFileName,
                videoBytes,
                cancellationToken);

            sceneVideos.Add(new GeneratedSceneVideo(
                sceneIndex + 1,
                segmentIndex + 1,
                clipDurations.Count,
                clipSeconds,
                image,
                inputBlobUri,
                outputBlobUri,
                videoId,
                outputFileName,
                videoBytes));
        }

        return sceneVideos;
    }

    private async Task<byte[]> ReadArtifactBytesAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken)
    {
        await using var stream = await artifactStore.OpenReadAsync(artifact.Uri, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        return memory.ToArray();
    }

    private async Task<NormalizedImage> NormalizeInputImageAsync(
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        var (width, height) = ParseSize(_options.Size);

        using var image = Image.Load(imageBytes);

        if (image.Width == width && image.Height == height)
        {
            return new NormalizedImage(imageBytes, "image/png");
        }

        logger.LogInformation(
            "Resizing Sora input image from {SourceWidth}x{SourceHeight} to {TargetWidth}x{TargetHeight}.",
            image.Width,
            image.Height,
            width,
            height);

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        await using var memory = new MemoryStream();
        await image.SaveAsync(memory, PngFormat.Instance, cancellationToken);

        return new NormalizedImage(memory.ToArray(), "image/png");
    }

    private async Task<string> CreateVideoAsync(
        string prompt,
        string imageFileName,
        string mediaType,
        byte[] imageBytes,
        string seconds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.OpenAiEndpoint.TrimEnd('/')}/openai/v1/videos");

        await AddAuthenticationAsync(request, cancellationToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_options.DeploymentName), "model");
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(_options.Size), "size");
        content.Add(new StringContent(seconds), "seconds");

        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        content.Add(imageContent, "input_reference", imageFileName);

        request.Content = content;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure Sora video create failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Azure Sora video create returned no id: {responseBody}");
        }

        return idElement.GetString()!;
    }

    private async Task<byte[]> WaitForVideoAndDownloadAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.TimeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await GetVideoStatusAsync(videoId, cancellationToken);

            logger.LogInformation(
                "Azure Sora video status. VideoId={VideoId}, Status={Status}, Progress={Progress}",
                videoId,
                status.Status,
                status.Progress);

            if (status.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadVideoAsync(videoId, cancellationToken);
            }

            if (status.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                status.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Azure Sora video ended with status {status.Status}: {status.Error}");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), cancellationToken);
        }

        throw new TimeoutException(
            $"Azure Sora video {videoId} did not complete within {_options.TimeoutSeconds} seconds.");
    }

    private async Task<SoraVideoStatus> GetVideoStatusAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.OpenAiEndpoint.TrimEnd('/')}/openai/v1/videos/{Uri.EscapeDataString(videoId)}");

        await AddAuthenticationAsync(request, cancellationToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure Sora video status failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var status = root.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "unknown"
            : "unknown";
        var progress = root.TryGetProperty("progress", out var progressElement) &&
            progressElement.TryGetInt32(out var progressValue)
                ? progressValue
                : 0;
        var error = root.TryGetProperty("error", out var errorElement)
            ? errorElement.ToString()
            : null;

        return new SoraVideoStatus(status, progress, error);
    }

    private async Task<byte[]> DownloadVideoAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.OpenAiEndpoint.TrimEnd('/')}/openai/v1/videos/{Uri.EscapeDataString(videoId)}/content?variant=video");

        await AddAuthenticationAsync(request, cancellationToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Azure Sora video download failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task AddAuthenticationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey) &&
            !string.IsNullOrWhiteSpace(_options.ApiKeyEnvironmentVariable))
        {
            apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("api-key", apiKey);
            return;
        }

        var bearerToken = _options.BearerToken;

        if (string.IsNullOrWhiteSpace(bearerToken) &&
            !string.IsNullOrWhiteSpace(_options.BearerTokenEnvironmentVariable))
        {
            bearerToken = Environment.GetEnvironmentVariable(_options.BearerTokenEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            bearerToken = (await _credential.GetTokenAsync(
                new TokenRequestContext([_options.TokenScope]),
                cancellationToken)).Token;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    private async Task<string> UploadInputImageAsync(
        AnimationPlanRequest request,
        string fileName,
        string mediaType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var blob = GetContainer(_options.InputContainerName)
            .GetBlobClient($"productions/{request.ProductionId}/{fileName}");

        await using var stream = new MemoryStream(content);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = mediaType
                }
            },
            cancellationToken);

        return blob.Uri.ToString();
    }

    private async Task<string> UploadOutputVideoAsync(
        AnimationPlanRequest request,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var blob = GetContainer(_options.OutputContainerName)
            .GetBlobClient($"productions/{request.ProductionId}/{fileName}");

        await using var stream = new MemoryStream(content);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "video/mp4"
                }
            },
            cancellationToken);

        return blob.Uri.ToString();
    }

    private BlobContainerClient GetContainer(string containerName)
    {
        var connectionString = _options.StorageConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString) &&
            !string.IsNullOrWhiteSpace(_options.StorageConnectionStringEnvironmentVariable))
        {
            connectionString = Environment.GetEnvironmentVariable(_options.StorageConnectionStringEnvironmentVariable);
        }

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
        }

        return new BlobServiceClient(new Uri(_options.BlobServiceEndpoint), _credential)
            .GetBlobContainerClient(containerName);
    }

    private static string BuildVideoPrompt(
        AnimationPlanRequest request,
        ArtifactReference image,
        int sceneIndex,
        int segmentIndex,
        int segmentCount,
        int clipSeconds)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Create a short family-friendly storybook animation from the provided image.");
        prompt.AppendLine("The video must be child-safe, wholesome, nonsexual, non-romantic, fully clothed, and suitable for all ages.");
        prompt.AppendLine("Use only gentle environmental motion: slow camera drift, subtle parallax, soft lighting shimmer, small background movement, and calm transitions.");
        prompt.AppendLine("Do not emphasize bodies, skin, intimacy, romance, sensuality, nudity, underwear, bathing, bed scenes, or physical attraction.");
        prompt.AppendLine("Do not add new people, public figures, copyrighted characters, logos, visible text, watermarks, violence, horror, weapons, drugs, or unsafe actions.");
        prompt.AppendLine("Silent visual clip only: do not generate narration, dialogue, speech, singing, music, subtitles, captions, or on-screen text.");
        prompt.AppendLine("If audio cannot be fully silent, use only soft neutral ambient sound with no spoken language.");
        prompt.AppendLine("Preserve the image as a clean illustrated picture-book scene. Keep characters modest, neutral, and non-suggestive.");
        prompt.AppendLine($"This is clip {segmentIndex + 1} of {segmentCount} for the current scene. Target duration for this clip is {clipSeconds} seconds.");

        if (segmentCount > 1)
        {
            prompt.AppendLine("Keep this clip focused on one small beat of the scene so it can be edited together with the other clips.");
            prompt.AppendLine("Do not attempt to cover the entire scene in this one clip.");
        }

        if (request.Attributes.TryGetValue("style", out var style) &&
            !string.IsNullOrWhiteSpace(style))
        {
            prompt.AppendLine($"Visual style: {style}.");
        }

        if (!string.IsNullOrWhiteSpace(request.StoryText))
        {
            prompt.AppendLine("Full story context for narrative continuity. Use it only to understand mood, timing, and action; do not add unsafe or adult details:");
            prompt.AppendLine(SanitizePromptText(ExtractStoryForPrompt(request.StoryText), 1600));
        }

        if (!string.IsNullOrWhiteSpace(image.Description))
        {
            prompt.AppendLine($"Reference image summary: {SanitizePromptText(image.Description)}");
        }

        var matchingShot = request.AnimationPlan?.Shots
            .OrderBy(shot => shot.Order)
            .ElementAtOrDefault(sceneIndex);

        if (matchingShot is not null)
        {
            prompt.AppendLine($"Shot title: {SanitizePromptText(matchingShot.Title)}");
            prompt.AppendLine($"Motion direction: {SanitizePromptText(matchingShot.Motion)}");
            prompt.AppendLine($"Camera direction: {SanitizePromptText(matchingShot.Camera)}");
            prompt.AppendLine($"Transition: {SanitizePromptText(matchingShot.Transition)}");
            prompt.AppendLine($"Shot notes: {SanitizePromptText(matchingShot.Notes)}");
        }

        var matchingScene = request.SceneBreakdown?.Scenes
            .OrderBy(scene => scene.Order)
            .ElementAtOrDefault(sceneIndex);

        if (matchingScene is not null)
        {
            prompt.AppendLine($"Scene title: {SanitizePromptText(matchingScene.Title)}");
            prompt.AppendLine($"Scene visual description: {SanitizePromptText(matchingScene.VisualDescription, 480)}");
            prompt.AppendLine($"Scene character action: {SanitizePromptText(matchingScene.CharacterAction, 360)}");
            prompt.AppendLine($"Mood and color: {SanitizePromptText(matchingScene.MoodAndColor)}");
            prompt.AppendLine($"Animator direction: {SanitizePromptText(matchingScene.AnimatorPrompt, 360)}");
        }

        prompt.AppendLine("Final safety instruction: produce only a gentle, silent, wholesome animated illustration with no spoken words and no sexual, suggestive, romantic, or adult content.");

        return prompt.ToString();
    }

    private static string ExtractStoryForPrompt(string storyText)
    {
        var marker = "Story draft:";
        var markerIndex = storyText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex >= 0)
        {
            return storyText[(markerIndex + marker.Length)..].Trim();
        }

        return storyText.Trim();
    }

    private static string SanitizePromptText(string text, int maxLength = 240)
    {
        var sanitized = text
            .Replace("性", "情緒", StringComparison.Ordinal)
            .Replace("性感", "溫和", StringComparison.Ordinal)
            .Replace("裸", "完整服裝", StringComparison.Ordinal)
            .Replace("親密", "友善", StringComparison.Ordinal)
            .Replace("浪漫", "溫暖", StringComparison.Ordinal)
            .Replace("sexual", "family-friendly", StringComparison.OrdinalIgnoreCase)
            .Replace("sexy", "wholesome", StringComparison.OrdinalIgnoreCase)
            .Replace("nude", "fully clothed", StringComparison.OrdinalIgnoreCase)
            .Replace("nudity", "fully clothed", StringComparison.OrdinalIgnoreCase)
            .Replace("intimate", "friendly", StringComparison.OrdinalIgnoreCase)
            .Replace("romantic", "warm", StringComparison.OrdinalIgnoreCase);

        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static string BuildArtifactContent(
        AnimationPlanRequest request,
        IReadOnlyList<GeneratedSceneVideo> sceneVideos)
    {
        var content = new StringBuilder();

        content.AppendLine($"# Production {request.ProductionId}");
        content.AppendLine();
        content.AppendLine("## Animator");
        content.AppendLine("Scene videos generated by Azure Sora image-to-video.");
        content.AppendLine();

        foreach (var sceneVideo in sceneVideos)
        {
            content.AppendLine($"### Scene {sceneVideo.SceneNumber:000}");
            content.AppendLine();
            content.AppendLine($"Clip: {sceneVideo.SegmentNumber:000}/{sceneVideo.SegmentCount:000}, {sceneVideo.ClipSeconds} seconds");
            content.AppendLine();
            content.AppendLine("Source image artifact:");
            content.AppendLine(sceneVideo.InputImage.Uri);
            content.AppendLine();
            content.AppendLine("Azure temporary input:");
            content.AppendLine(sceneVideo.InputBlobUri);
            content.AppendLine();
            content.AppendLine("Azure temporary output:");
            content.AppendLine(sceneVideo.OutputBlobUri);
            content.AppendLine();
            content.AppendLine("Sora video id:");
            content.AppendLine(sceneVideo.VideoId);
            content.AppendLine();
            content.AppendLine("Local artifact written by worker:");
            content.AppendLine(sceneVideo.OutputFileName);
            content.AppendLine();
        }

        return content.ToString();
    }

    private static string BuildInputFileName(ArtifactReference image, string mediaType)
    {
        var extension = mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
            ? ".jpg"
            : ".png";

        if (Uri.TryCreate(image.Uri, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.AbsolutePath);

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return $"scene-reference{extension}";
    }

    private static string BuildOutputFileName(string inputFileName, int sceneIndex, int segmentIndex, int segmentCount)
    {
        var baseName = Path.GetFileNameWithoutExtension(inputFileName);

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"scene-{sceneIndex + 1:000}";
        }

        return segmentCount == 1
            ? $"{baseName}.mp4"
            : $"{baseName}-clip-{segmentIndex + 1:000}.mp4";
    }

    private IReadOnlyList<int> BuildClipDurations(AnimationPlanRequest request, int sceneIndex)
    {
        var targetSeconds = request.AnimationPlan?.Shots
            .OrderBy(shot => shot.Order)
            .ElementAtOrDefault(sceneIndex)
            ?.DurationSeconds;

        if (targetSeconds is null or <= 0 &&
            int.TryParse(_options.Seconds, CultureInfo.InvariantCulture, out var fallbackSeconds))
        {
            targetSeconds = fallbackSeconds;
        }

        targetSeconds ??= 4;

        return SplitDurationIntoSoraClips(targetSeconds.Value, _options.MaxClipSeconds);
    }

    private static IReadOnlyList<int> SplitDurationIntoSoraClips(int targetSeconds, int maxClipSeconds)
    {
        var allowedDurations = new[] { 12, 8, 4 };
        var effectiveMax = allowedDurations
            .Where(duration => duration <= Math.Clamp(maxClipSeconds, 4, 12))
            .DefaultIfEmpty(4)
            .Max();

        var usableDurations = allowedDurations
            .Where(duration => duration <= effectiveMax)
            .ToArray();

        var clips = new List<int>();
        var remaining = Math.Max(4, targetSeconds);

        while (remaining > 0)
        {
            var clip = usableDurations.FirstOrDefault(duration => duration <= remaining);

            if (clip == 0)
            {
                clip = usableDurations
                    .OrderBy(duration => Math.Abs(duration - remaining))
                    .First();
            }

            clips.Add(clip);
            remaining -= clip;
        }

        return clips;
    }

    private static (int Width, int Height) ParseSize(string size)
    {
        var parts = size.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height) ||
            width <= 0 ||
            height <= 0)
        {
            throw new InvalidOperationException(
                $"AzureSoraVideo:Size must be formatted as WIDTHxHEIGHT. Current value: '{size}'.");
        }

        return (width, height);
    }

    private sealed record NormalizedImage(
        byte[] Content,
        string MediaType);

    private sealed record GeneratedSceneVideo(
        int SceneNumber,
        int SegmentNumber,
        int SegmentCount,
        int ClipSeconds,
        ArtifactReference InputImage,
        string InputBlobUri,
        string OutputBlobUri,
        string VideoId,
        string OutputFileName,
        byte[] VideoBytes);

    private sealed record SoraVideoStatus(
        string Status,
        int Progress,
        string? Error);
}
