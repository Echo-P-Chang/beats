using System.Text.Json;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Media;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Beats.Production.Middleware.Persistence;

public sealed class MySqlProductionRepository(IOptions<DatabaseOptions> options) : IProductionRepository
{
    private readonly string _connectionString = options.Value.GetConnectionString();

    public async Task CreateProductionAsync(
        Guid productionId,
        string prompt,
        string? style,
        int targetWordCount,
        int? durationSeconds,
        string status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Productions (
                ProductionId,
                Prompt,
                Style,
                TargetWordCount,
                DurationSeconds,
                Status,
                CreatedAt,
                UpdatedAt)
            VALUES (
                @ProductionId,
                @Prompt,
                @Style,
                @TargetWordCount,
                @DurationSeconds,
                @Status,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6));
            """;

        Add(command, "@ProductionId", productionId);
        Add(command, "@Prompt", prompt);
        Add(command, "@Style", style);
        Add(command, "@TargetWordCount", targetWordCount);
        Add(command, "@DurationSeconds", durationSeconds);
        Add(command, "@Status", status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProductionRecord?> GetProductionAsync(
        Guid productionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT ProductionId, Prompt, Style, TargetWordCount, DurationSeconds, Status, CreatedAt, UpdatedAt
            FROM Productions
            WHERE ProductionId = @ProductionId;
            """;

        Add(command, "@ProductionId", productionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProductionRecord(
            GetGuid(reader, 0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.GetString(5),
            ToDateTimeOffset(reader.GetDateTime(6)),
            ToDateTimeOffset(reader.GetDateTime(7)));
    }

    public async Task UpdateProductionStatusAsync(
        Guid productionId,
        string status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Productions
            SET Status = @Status,
                UpdatedAt = UTC_TIMESTAMP(6)
            WHERE ProductionId = @ProductionId;
            """;

        Add(command, "@ProductionId", productionId);
        Add(command, "@Status", status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordEventAsync<TPayload>(
        EventEnvelope<TPayload> message,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT IGNORE INTO ProductionEvents (
                EventId,
                ProductionId,
                EventType,
                CorrelationId,
                CausationId,
                Producer,
                SchemaVersion,
                CreatedAt,
                PayloadJson)
            VALUES (
                @EventId,
                @ProductionId,
                @EventType,
                @CorrelationId,
                @CausationId,
                @Producer,
                @SchemaVersion,
                @CreatedAt,
                @PayloadJson);
            """;

        Add(command, "@EventId", message.EventId);
        Add(command, "@ProductionId", message.ProductionId);
        Add(command, "@EventType", message.EventType);
        Add(command, "@CorrelationId", message.CorrelationId);
        Add(command, "@CausationId", message.CausationId);
        Add(command, "@Producer", message.Producer);
        Add(command, "@SchemaVersion", message.SchemaVersion);
        Add(command, "@CreatedAt", message.CreatedAt.UtcDateTime);
        Add(command, "@PayloadJson", JsonSerializer.Serialize(message.Payload));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordArtifactAsync(
        Guid productionId,
        string agentRole,
        ArtifactReference artifact,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Artifacts (
                ArtifactId,
                ProductionId,
                AgentRole,
                ArtifactUri,
                MediaType,
                Description,
                CreatedAt)
            VALUES (
                @ArtifactId,
                @ProductionId,
                @AgentRole,
                @ArtifactUri,
                @MediaType,
                @Description,
                UTC_TIMESTAMP(6));
            """;

        Add(command, "@ArtifactId", Guid.NewGuid());
        Add(command, "@ProductionId", productionId);
        Add(command, "@AgentRole", agentRole);
        Add(command, "@ArtifactUri", artifact.Uri);
        Add(command, "@MediaType", artifact.MediaType);
        Add(command, "@Description", artifact.Description);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAgentRunAsync(
        AgentRunRecord record,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO AgentRuns (
                AgentRunId,
                ProductionId,
                AgentRole,
                ConsumedEventId,
                PublishedEventId,
                InputArtifactUri,
                OutputArtifactUri,
                Status,
                StartedAt,
                CompletedAt,
                Notes)
            VALUES (
                @AgentRunId,
                @ProductionId,
                @AgentRole,
                @ConsumedEventId,
                @PublishedEventId,
                @InputArtifactUri,
                @OutputArtifactUri,
                @Status,
                @StartedAt,
                @CompletedAt,
                @Notes);
            """;

        Add(command, "@AgentRunId", record.AgentRunId);
        Add(command, "@ProductionId", record.ProductionId);
        Add(command, "@AgentRole", record.AgentRole);
        Add(command, "@ConsumedEventId", record.ConsumedEventId);
        Add(command, "@PublishedEventId", record.PublishedEventId);
        Add(command, "@InputArtifactUri", record.InputArtifactUri);
        Add(command, "@OutputArtifactUri", record.OutputArtifactUri);
        Add(command, "@Status", record.Status);
        Add(command, "@StartedAt", record.StartedAt.UtcDateTime);
        Add(command, "@CompletedAt", record.CompletedAt.UtcDateTime);
        Add(command, "@Notes", record.Notes);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var connection = new MySqlConnection(_connectionString);

            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (MySqlException) when (attempt < maxAttempts)
            {
                await connection.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        var finalConnection = new MySqlConnection(_connectionString);
        await finalConnection.OpenAsync(cancellationToken);

        return finalConnection;
    }

    private static void Add(MySqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static Guid GetGuid(MySqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);

        return value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            _ => Guid.Parse(Convert.ToString(value) ?? throw new InvalidOperationException("Database value is null."))
        };
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
