using System.Diagnostics;
using System.Text;
using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Flows;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using Beats.Production.Middleware.Specifications;
using MassTransit;

namespace Beats.Agents.Editor;

public sealed class SceneAnimationsCreatedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    IProductionFlow productionFlow,
    IProductionRepository productionRepository,
    IProductionSpecValidator productionSpecValidator,
    ILogger<SceneAnimationsCreatedConsumer> logger) : IConsumer<EventEnvelope<CommonPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<CommonPayload>> context)
    {
        var incoming = context.Message;

        if (!productionFlow.IsSubscribedTo(AgentRoles.Editor, incoming.EventType))
        {
            return;
        }

        var specValidation = await productionSpecValidator.ValidateAsync(
            incoming,
            AgentRoles.Editor,
            context.CancellationToken);
        EnsureValidSpec(specValidation);

        var startedAt = DateTimeOffset.UtcNow;
        var videoInputArtifacts = GetVideoArtifacts(incoming);
        var videoInputArtifact = videoInputArtifacts.FirstOrDefault();
        var reportArtifact = GetOptionalArtifact(incoming, "animationReportUri", "animationPlanUri");
        var storyArtifact = GetOptionalArtifact(incoming, "storyUri");
        var inputArtifact = videoInputArtifact ?? reportArtifact ?? incoming.Payload.Artifacts.First();

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, AnimationCount={AnimationCount}",
            AgentRoles.Editor,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Attributes.TryGetValue("animationCount", out var count) ? count : incoming.Payload.Artifacts.Count);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        ArtifactReference finalVideoArtifact;

        if (videoInputArtifacts.Count > 1)
        {
            await using var finalVideoStream = await ConcatenateVideosAsync(
                videoInputArtifacts,
                incoming.ProductionId,
                context.CancellationToken);
            var finalVideoUri = await artifactStore.SaveAsync(
                finalVideoStream,
                $"productions/{incoming.ProductionId}/04-editor/final-video.mp4",
                "video/mp4",
                context.CancellationToken);

            finalVideoArtifact = new ArtifactReference(
                finalVideoUri,
                "video/mp4",
                $"Final video assembled by the editor agent from {videoInputArtifacts.Count} animator clips.");
        }
        else if (videoInputArtifact is not null)
        {
            await using var inputStream = await artifactStore.OpenReadAsync(
                videoInputArtifact.Uri,
                context.CancellationToken);
            var finalVideoUri = await artifactStore.SaveAsync(
                inputStream,
                $"productions/{incoming.ProductionId}/04-editor/final-video.mp4",
                videoInputArtifact.MediaType,
                context.CancellationToken);

            finalVideoArtifact = new ArtifactReference(
                finalVideoUri,
                videoInputArtifact.MediaType,
                "Final video assembled by the editor agent from animator video output.");
        }
        else
        {
            var report = reportArtifact is null
                ? "No animation report artifact was provided."
                : await ArtifactText.ReadAsync(
                    artifactStore,
                    reportArtifact.Uri,
                    context.CancellationToken);

            var finalCutPlan = $"""
                # Production {incoming.ProductionId}

                ## Editor
                Final cut plan:
                The editor did not receive a video artifact, so this stage publishes a text final-cut plan.

                Source animation report:
                {report.Trim()}
                """;

            var finalCutPlanUri = await ArtifactText.SaveAsync(
                artifactStore,
                finalCutPlan,
                $"productions/{incoming.ProductionId}/04-editor/final-cut-plan.txt",
                context.CancellationToken);

            finalVideoArtifact = new ArtifactReference(
                finalCutPlanUri,
                "text/plain",
                "Final cut plan generated because no animator video artifact was available.");
        }

        var voiceOver = await BuildVoiceOverScriptAsync(
            incoming.ProductionId,
            storyArtifact,
            context.CancellationToken);

        var voiceOverUri = await ArtifactText.SaveAsync(
            artifactStore,
            voiceOver,
            $"productions/{incoming.ProductionId}/04-editor/voiceover-script.txt",
            context.CancellationToken);

        var voiceOverArtifact = new ArtifactReference(
            voiceOverUri,
            "text/plain",
            "Voiceover script prepared by the editor agent.");

        var publication = productionFlow.GetRequiredPublication<CommonPayload>(
            AgentRoles.Editor,
            incoming.EventType);

        var payload = CommonPayload.Create(
            publication.EventType,
            AgentRoles.Editor,
            attributes: new Dictionary<string, string>(incoming.Payload.Attributes)
            {
                ["videoUri"] = finalVideoArtifact.Uri,
                ["voiceOverUri"] = voiceOverArtifact.Uri
            },
            artifacts: [finalVideoArtifact, voiceOverArtifact]);

        var outgoing = EventEnvelope<CommonPayload>.Create(
            publication.EventType,
            incoming.ProductionId,
            AgentRoles.Editor,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Editor,
            finalVideoArtifact,
            context.CancellationToken);
        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Editor,
            voiceOverArtifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Editor,
                incoming.EventId,
                outgoing.EventId,
                inputArtifact.Uri,
                finalVideoArtifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                videoInputArtifact is null
                    ? "Created a final cut plan and voiceover script because no video artifact was available."
                    : videoInputArtifacts.Count > 1
                        ? $"Concatenated {videoInputArtifacts.Count} animator video clips into a final-video artifact and added a voiceover script."
                        : "Copied animator video output into a final-video artifact and added a voiceover script."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.FinalVideoCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }

    private static void EnsureValidSpec(ProductionSpecValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Production spec preflight failed for {validation.Stage.AgentRole}: {string.Join("; ", validation.Errors)}");
        }
    }

    private static ArtifactReference? GetOptionalArtifact(
        EventEnvelope<CommonPayload> incoming,
        params string[] outputKeys)
    {
        foreach (var outputKey in outputKeys)
        {
            if (!incoming.Payload.Attributes.TryGetValue(outputKey, out var uri) ||
                string.IsNullOrWhiteSpace(uri))
            {
                continue;
            }

            return incoming.Payload.Artifacts.FirstOrDefault(artifact =>
                artifact.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)) ??
                new ArtifactReference(uri, "application/octet-stream");
        }

        return null;
    }

    private static IReadOnlyList<ArtifactReference> GetVideoArtifacts(EventEnvelope<CommonPayload> incoming)
    {
        if (incoming.Payload.Attributes.TryGetValue("animationVideoUris", out var videoUris) &&
            !string.IsNullOrWhiteSpace(videoUris))
        {
            return videoUris
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(uri => incoming.Payload.Artifacts.FirstOrDefault(artifact =>
                    artifact.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)) ??
                    new ArtifactReference(uri, "video/mp4"))
                .ToArray();
        }

        return incoming.Payload.Artifacts
            .Where(artifact => artifact.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private async Task<Stream> ConcatenateVideosAsync(
        IReadOnlyList<ArtifactReference> videoArtifacts,
        Guid productionId,
        CancellationToken cancellationToken)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "beats-editor", productionId.ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var concatList = new StringBuilder();

            for (var index = 0; index < videoArtifacts.Count; index++)
            {
                var clipPath = Path.Combine(workDir, $"clip-{index + 1:000}.mp4");
                await using (var input = await artifactStore.OpenReadAsync(videoArtifacts[index].Uri, cancellationToken))
                await using (var output = File.Create(clipPath))
                {
                    await input.CopyToAsync(output, cancellationToken);
                }

                concatList.AppendLine($"file '{EscapeFfmpegConcatPath(clipPath)}'");
            }

            var listPath = Path.Combine(workDir, "clips.txt");
            await File.WriteAllTextAsync(
                listPath,
                concatList.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var outputPath = Path.Combine(workDir, "final-video.mp4");
            await RunFfmpegAsync(
                $"-y -f concat -safe 0 -i {QuoteArgument(listPath)} -c copy {QuoteArgument(outputPath)}",
                cancellationToken);

            var memory = new MemoryStream(await File.ReadAllBytesAsync(outputPath, cancellationToken));
            memory.Position = 0;
            return memory;
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Unable to delete editor work directory {WorkDir}.", workDir);
            }
        }
    }

    private static async Task RunFfmpegAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg concat failed with exit code {process.ExitCode}. stdout={standardOutput} stderr={standardError}");
        }
    }

    private static string EscapeFfmpegConcatPath(string path)
    {
        return path.Replace("'", "'\\''", StringComparison.Ordinal);
    }

    private static string QuoteArgument(string argument)
    {
        return $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private async Task<string> BuildVoiceOverScriptAsync(
        Guid productionId,
        ArtifactReference? storyArtifact,
        CancellationToken cancellationToken)
    {
        var storyText = storyArtifact is null
            ? "目前沒有收到 story artifact。請旁白員以畫面節奏進行溫和口白。"
            : await ArtifactText.ReadAsync(
                artifactStore,
                storyArtifact.Uri,
                cancellationToken);
        var narrationText = ExtractNarrationText(storyText);

        return $"""
            # Production {productionId}

            ## Voiceover
            Language: 台灣繁體中文
            Delivery: 台灣華語自然口吻，溫柔、清楚、適合親子共賞。避免粵語、簡體字與中國大陸慣用語。

            Narration script:
            {narrationText}

            Direction:
            旁白請配合每個場景的轉場留白，語速穩定，句尾留一點呼吸感。若後續接 TTS，請使用台灣華語女聲或中性溫暖聲線。
            """;
    }

    private static string ExtractNarrationText(string storyText)
    {
        var marker = "Story draft:";
        var markerIndex = storyText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var source = markerIndex >= 0
            ? storyText[(markerIndex + marker.Length)..]
            : storyText;
        var endMarkerIndex = source.IndexOf("## 場景拆解", StringComparison.Ordinal);

        if (endMarkerIndex >= 0)
        {
            source = source[..endMarkerIndex];
        }

        return source
            .Replace("【故事完】", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
