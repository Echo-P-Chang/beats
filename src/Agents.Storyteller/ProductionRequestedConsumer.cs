using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Events.Payloads;
using Beats.Production.Flows;
using Beats.Production.Contracts.Media;
using Beats.Production.Contracts.Productions;
using Beats.Production.Middleware.Ai;
using Beats.Production.Middleware.Artifacts;
using Beats.Production.Middleware.Eventing;
using Beats.Production.Middleware.Persistence;
using MassTransit;
using System.Text;
using System.Text.RegularExpressions;

namespace Beats.Agents.Storyteller;

public sealed partial class ProductionRequestedConsumer(
    IArtifactStore artifactStore,
    IEventPublisher eventPublisher,
    ITextGenerationClient textGenerationClient,
    IProductionFlow productionFlow,
    IProductionRepository productionRepository,
    ILogger<ProductionRequestedConsumer> logger) : IConsumer<EventEnvelope<ProductionRequestedPayload>>
{
    public async Task Consume(ConsumeContext<EventEnvelope<ProductionRequestedPayload>> context)
    {
        var incoming = context.Message;
        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation(
            "{AgentRole} consumed {EventType}. ProductionId={ProductionId}, Prompt={Prompt}",
            AgentRoles.Storyteller,
            incoming.EventType,
            incoming.ProductionId,
            incoming.Payload.Prompt);

        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);

        var generation = await GenerateValidatedStoryAsync(
            incoming,
            context.CancellationToken);

        var manuscript = $"""
            # Production {incoming.ProductionId}

            ## Storyteller
            Prompt: {incoming.Payload.Prompt}
            Style: {incoming.Payload.Style ?? "not specified"}
            Target word count: {incoming.Payload.TargetWordCount}
            Target duration seconds: {incoming.Payload.DurationSeconds?.ToString() ?? "not specified"}
            Model: {generation.Model}
            Generation duration: {generation.TotalDuration?.ToString() ?? "unknown"}
            Finish reason: {generation.FinishReason ?? "unknown"}

            Story draft:
            {generation.Text.Trim()}
            """;

        var artifactUri = await ArtifactText.SaveAsync(
            artifactStore,
            manuscript,
            $"productions/{incoming.ProductionId}/01-storyteller/manuscript.txt",
            context.CancellationToken);

        var manuscriptArtifact = new ArtifactReference(
            artifactUri,
            "text/plain",
            "Manuscript initialized by the storyteller agent.");

        var payload = new StoryCreatedPayload(
            manuscriptArtifact,
            manuscriptArtifact);

        var publication = productionFlow.GetRequiredPublication<StoryCreatedPayload>(
            AgentRoles.Storyteller,
            incoming.EventType);

        var outgoing = EventEnvelope<StoryCreatedPayload>.Create(
            publication.EventType,
            incoming.ProductionId,
            AgentRoles.Storyteller,
            payload,
            incoming.CorrelationId,
            incoming.EventId);

        await productionRepository.RecordArtifactAsync(
            incoming.ProductionId,
            AgentRoles.Storyteller,
            manuscriptArtifact,
            context.CancellationToken);

        await productionRepository.RecordAgentRunAsync(
            new AgentRunRecord(
                Guid.NewGuid(),
                incoming.ProductionId,
                AgentRoles.Storyteller,
                incoming.EventId,
                outgoing.EventId,
                null,
                manuscriptArtifact.Uri,
                "Completed",
                startedAt,
                DateTimeOffset.UtcNow,
                $"Initialized the evolving text artifact with Ollama model {generation.Model}. Validation={ValidateStoryDraft(generation.Text).Summary}."),
            context.CancellationToken);

        await productionRepository.RecordEventAsync(outgoing, context.CancellationToken);
        await productionRepository.UpdateProductionStatusAsync(
            incoming.ProductionId,
            ProductionStatus.StoryCreated.ToString(),
            context.CancellationToken);

        await eventPublisher.PublishAsync(outgoing, context.CancellationToken);
    }

    private static string BuildStoryPrompt(EventEnvelope<ProductionRequestedPayload> incoming)
    {
        return $"""
            請根據以下需求創作一份完整故事稿。請務必使用台灣繁體中文，不要使用粵語、簡體字或中國大陸慣用語。

            ProductionId: {incoming.ProductionId}
            使用者需求: {incoming.Payload.Prompt}
            風格: {incoming.Payload.Style ?? "未指定"}
            目標字數: 約 {incoming.Payload.TargetWordCount} 字
            影片長度: {(incoming.Payload.DurationSeconds is null ? "未指定" : $"{incoming.Payload.DurationSeconds} 秒")}

            請嚴格依照以下格式輸出，不要加入格式外的文字：

            # 故事標題
            請給故事一個明確、適合繪本或短片的標題。標題只要一行。

            ## 完整故事
            這一節只能寫故事本文，不要條列。
            請寫約 {incoming.Payload.TargetWordCount} 字，至少 6 個自然段，最多 9 個自然段。
            故事必須完整收尾，不可以停在半句、半段或未完成的情節。
            故事結尾必須明確解決主角遇到的問題。
            寫完故事後，務必繼續輸出「## 場景拆解」，不可只停在故事本文。

            ## 場景拆解
            請拆成剛好 3 個場景。場景拆解要精簡，每個欄位 1 句即可。
            三個場景都必須完整輸出，不可省略任何一個欄位。

            ### 場景一：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            ### 場景二：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            ### 場景三：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            最後一行請輸出：
            【故事完】
            """;
    }

    private async Task<TextGenerationResponse> GenerateValidatedStoryAsync(
        EventEnvelope<ProductionRequestedPayload> incoming,
        CancellationToken cancellationToken)
    {
        var generation = await textGenerationClient.GenerateAsync(
            new TextGenerationRequest(
                BuildStoryPrompt(incoming),
                SystemPrompt: StorytellerSystemPrompt,
                Temperature: 0.55,
                NumPredict: 8192),
            cancellationToken);

        var validation = ValidateStoryDraft(generation.Text);

        if (validation.IsValid)
        {
            return generation;
        }

        logger.LogWarning(
            "{AgentRole} generated an incomplete story draft. ProductionId={ProductionId}, Problems={Problems}",
            AgentRoles.Storyteller,
            incoming.ProductionId,
            validation.Summary);

        var repair = await textGenerationClient.GenerateAsync(
            new TextGenerationRequest(
                BuildRepairPrompt(incoming, generation.Text, validation),
                SystemPrompt: StorytellerSystemPrompt,
                Temperature: 0.2,
                NumPredict: 6144),
            cancellationToken);

        var repairValidation = ValidateStoryDraft(repair.Text);

        if (repairValidation.IsValid)
        {
            return repair;
        }

        logger.LogWarning(
            "{AgentRole} story repair still failed validation. ProductionId={ProductionId}, Problems={Problems}",
            AgentRoles.Storyteller,
            incoming.ProductionId,
            repairValidation.Summary);

        var fallback = BuildFallbackStructuredDraft(incoming, repair.Text);

        return repair with
        {
            Text = fallback,
            FinishReason = $"{repair.FinishReason ?? "unknown"}; local-format-fallback"
        };
    }

    private static string BuildRepairPrompt(
        EventEnvelope<ProductionRequestedPayload> incoming,
        string draft,
        StoryDraftValidation validation)
    {
        return $"""
            下面是一份說書人草稿，但它不符合格式需求。
            問題：{validation.Summary}

            請把它修復成完整稿，務必保留故事內容，並補上缺少的場景拆解。
            請使用台灣繁體中文與台灣華語書面語，不可使用粵語、簡體字或中國大陸慣用語。

            必須輸出以下結構：

            # 故事標題

            ## 完整故事
            約 {incoming.Payload.TargetWordCount} 字，6 到 9 個自然段，故事必須完整收尾。

            ## 場景拆解

            ### 場景一：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            ### 場景二：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            ### 場景三：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            最後一行必須是：
            【故事完】

            草稿如下：
            {draft.Trim()}
            """;
    }

    private static StoryDraftValidation ValidateStoryDraft(string draft)
    {
        var problems = new List<string>();
        var normalized = draft.Trim();

        if (!normalized.Contains("## 完整故事", StringComparison.Ordinal))
        {
            problems.Add("missing-full-story-section");
        }

        if (!normalized.Contains("## 場景拆解", StringComparison.Ordinal))
        {
            problems.Add("missing-scene-breakdown-section");
        }

        var sceneCount = CountSceneHeadings(normalized);

        if (sceneCount < 3)
        {
            problems.Add($"too-few-scenes:{sceneCount}");
        }

        if (!normalized.EndsWith("【故事完】", StringComparison.Ordinal))
        {
            problems.Add("missing-story-end-marker");
        }

        return new StoryDraftValidation(
            problems.Count == 0,
            sceneCount,
            problems.Count == 0 ? "valid" : string.Join(',', problems));
    }

    private static int CountSceneHeadings(string draft)
    {
        var sceneSectionIndex = draft.IndexOf("## 場景拆解", StringComparison.Ordinal);
        var source = sceneSectionIndex >= 0 ? draft[sceneSectionIndex..] : draft;

        return SceneHeadingRegex().Matches(source).Count;
    }

    private static string BuildFallbackStructuredDraft(
        EventEnvelope<ProductionRequestedPayload> incoming,
        string draft)
    {
        var title = ExtractTitle(draft);
        var story = ExtractStoryBody(draft);
        var paragraphs = SplitParagraphs(story).Take(9).ToArray();

        if (paragraphs.Length == 0)
        {
            paragraphs = [story.Trim()];
        }

        var sceneSeeds = paragraphs.Length >= 3
            ? new[] { paragraphs.First(), paragraphs[paragraphs.Length / 2], paragraphs.Last() }
            : new[] { paragraphs.First(), paragraphs.First(), paragraphs.Last() };

        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine("## 完整故事");
        builder.AppendLine(string.Join(Environment.NewLine + Environment.NewLine, paragraphs));
        builder.AppendLine();
        builder.AppendLine("## 場景拆解");
        builder.AppendLine();

        for (var index = 0; index < 3; index++)
        {
            var sceneNumber = index switch
            {
                0 => "一",
                1 => "二",
                _ => "三"
            };

            var mood = index switch
            {
                0 => "明亮而帶有期待",
                1 => "轉折、緊張且富有層次",
                _ => "溫暖、安定並帶有完成感"
            };

            builder.AppendLine($"### 場景{sceneNumber}：{SceneTitles[index]}");
            builder.AppendLine($"- 畫面描述：以「{Shorten(sceneSeeds[index], 48)}」為核心畫面，呈現{incoming.Payload.Style ?? "水彩繪本風"}的視覺氛圍。");
            builder.AppendLine("- 角色動作：主角在畫面中央做出明確行動，讓觀眾看懂當下的選擇。");
            builder.AppendLine($"- 情緒與色彩：{mood}，色彩保持柔和、乾淨且適合動畫延展。");
            builder.AppendLine("- 給繪圖師的提示：維持一致角色造型與光線方向，不要加入文字、浮水印或標誌。");
            builder.AppendLine("- 給動畫師的提示：使用緩慢推鏡與輕微 parallax，讓場景有呼吸感。");
            builder.AppendLine();
        }

        builder.AppendLine("【故事完】");

        return builder.ToString().Trim();
    }

    private static string ExtractTitle(string draft)
    {
        var match = Regex.Match(draft, @"^#\s*(?<title>.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups["title"].Value.Trim() : "未命名的故事";
    }

    private static string ExtractStoryBody(string draft)
    {
        var fullStoryIndex = draft.IndexOf("## 完整故事", StringComparison.Ordinal);

        if (fullStoryIndex >= 0)
        {
            var start = fullStoryIndex + "## 完整故事".Length;
            var sceneIndex = draft.IndexOf("## 場景拆解", start, StringComparison.Ordinal);
            return sceneIndex >= 0 ? draft[start..sceneIndex].Trim() : draft[start..].Trim();
        }

        var lines = draft
            .Split('\n')
            .Where(line =>
            {
                var trimmed = line.Trim();
                return trimmed.Length > 0 &&
                    !trimmed.StartsWith('#') &&
                    !trimmed.StartsWith("【故事完】");
            });

        return string.Join(Environment.NewLine + Environment.NewLine, lines).Trim();
    }

    private static string[] SplitParagraphs(string text)
    {
        return text
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .ToArray();
    }

    private static string Shorten(string text, int maxLength)
    {
        var normalized = Regex.Replace(text.Trim(), @"\s+", string.Empty);

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private const string StorytellerSystemPrompt = """
        你是 event-driven AI production pipeline 裡的「說書人」agent。
        你的任務是交付一篇完整、可閱讀、可被下游 agent 解析的故事稿。

        重要規則：
        - 只輸出最終故事稿，不要輸出分析、思考過程、規劃筆記、hidden reasoning、channel tags。
        - 使用台灣繁體中文與台灣華語書面語。
        - 不可使用粵語詞或香港口語，例如：佢、嘅、啲、喺、唔、咁、冇、係、嘢、睇、嗰、咗。
        - 不可使用簡體字或中國大陸慣用語，例如：视频、里面、公交、质量、后台、账号。
        - 必須輸出 `## 完整故事` 與 `## 場景拆解`。
        - 場景拆解必須有 3 個 `### 場景...` 小節。
        - 最後一行必須是：【故事完】
        """;

    private static readonly string[] SceneTitles =
    [
        "開場與召喚",
        "轉折與行動",
        "抵達與收束"
    ];

    [GeneratedRegex(@"(?m)^###\s*場\S*?[一二三四五六七八九十\d]+[：:\s-]*.+$")]
    private static partial Regex SceneHeadingRegex();

    private sealed record StoryDraftValidation(
        bool IsValid,
        int SceneCount,
        string Summary);
}
