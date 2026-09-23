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

namespace Beats.Agents.Storyteller;

public sealed class ProductionRequestedConsumer(
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

        var generation = await textGenerationClient.GenerateAsync(
            new TextGenerationRequest(
                BuildStoryPrompt(incoming),
                SystemPrompt: """
                    你是 event-driven AI production pipeline 裡的「說書人」agent。
                    你的任務是交付一篇完整、可閱讀的故事，而不是摘要、草稿或創作分析。

                    重要規則：
                    - 只輸出最終故事稿，不要輸出分析、思考過程、規劃筆記、hidden reasoning、channel tags。
                    - 使用台灣繁體中文與台灣華語書面語。
                    - 不可使用粵語詞或香港口語，例如：佢、嘅、啲、喺、唔、咁、冇、係、嘢。
                    - 不可使用簡體字或中國大陸慣用語，例如：视频、里面、公交、质量、后台、账号。
                    - 故事本文是最重要的交付物，必須完整，有開頭、發展、轉折、高潮與結尾。
                    - 先寫完整故事，再寫場景拆解。若長度不夠，縮短場景拆解，不可縮短或截斷故事。
                    - 不要寫「以下是」或任何前言，直接從標題開始。
                    - 最後一行必須是：【故事完】
                    """,
                Temperature: 0.65,
                NumPredict: 8192),
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
                $"Initialized the evolving text artifact with Ollama model {generation.Model}."),
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
            請給故事一個明確、適合繪本或短片的標題。

            ## 完整故事
            這一節只能寫故事本文，不要條列。
            請寫約 {incoming.Payload.TargetWordCount} 字，至少 6 個自然段。
            故事必須完整收尾，不可以停在半句、半段或未完成的情節。
            故事結尾必須明確解決主角遇到的問題。

            ## 場景拆解
            請拆成 3 到 5 個場景。場景拆解要精簡，每個欄位 1 句即可：

            ### 場景一：場景名稱
            - 畫面描述：
            - 角色動作：
            - 情緒與色彩：
            - 給繪圖師的提示：
            - 給動畫師的提示：

            最後一行請輸出：
            【故事完】
            """;
    }
}
