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
                    You are the Storyteller agent in an event-driven AI production pipeline.
                    Write in Traditional Chinese.
                    Do not reveal chain-of-thought, hidden reasoning, analysis notes, or channel tags.
                    Produce a complete story draft and clearly separate the story into scenes.
                    Keep the output useful for downstream illustrator, animator, editor, and reviewer agents.
                    """,
                Temperature: 0.8,
                NumPredict: 4096),
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
            請根據以下需求創作故事，並讓後續 agent 可以直接使用：

            ProductionId: {incoming.ProductionId}
            使用者需求: {incoming.Payload.Prompt}
            風格: {incoming.Payload.Style ?? "未指定"}
            目標字數: 約 {incoming.Payload.TargetWordCount} 字
            影片長度: {(incoming.Payload.DurationSeconds is null ? "未指定" : $"{incoming.Payload.DurationSeconds} 秒")}

            請輸出：
            1. 故事標題
            2. 完整故事本文
            3. 場景拆解，至少 3 個場景，每個場景包含：
               - 場景名稱
               - 畫面描述
               - 角色動作
               - 情緒與色彩
               - 給繪圖師與動畫師的提示
            """;
    }
}
