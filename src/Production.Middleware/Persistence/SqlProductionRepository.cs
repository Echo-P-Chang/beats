using System.Data;
using System.Text.Json;
using Beats.Production.Contracts.Events;
using Beats.Production.Contracts.Media;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Beats.Production.Middleware.Persistence;

public sealed class SqlProductionRepository(IOptions<DatabaseOptions> options) : IProductionRepository
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
            INSERT INTO dbo.Productions (
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
                SYSUTCDATETIME(),
                SYSUTCDATETIME());
            """;

        Add(command, "@ProductionId", SqlDbType.UniqueIdentifier, productionId);
        Add(command, "@Prompt", SqlDbType.NVarChar, prompt);
        Add(command, "@Style", SqlDbType.NVarChar, (object?)style ?? DBNull.Value);
        Add(command, "@TargetWordCount", SqlDbType.Int, targetWordCount);
        Add(command, "@DurationSeconds", SqlDbType.Int, (object?)durationSeconds ?? DBNull.Value);
        Add(command, "@Status", SqlDbType.NVarChar, status);

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
            FROM dbo.Productions
            WHERE ProductionId = @ProductionId;
            """;

        Add(command, "@ProductionId", SqlDbType.UniqueIdentifier, productionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProductionRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.GetString(5),
            reader.GetDateTimeOffset(6),
            reader.GetDateTimeOffset(7));
    }

    public async Task UpdateProductionStatusAsync(
        Guid productionId,
        string status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE dbo.Productions
            SET Status = @Status,
                UpdatedAt = SYSUTCDATETIME()
            WHERE ProductionId = @ProductionId;
            """;

        Add(command, "@ProductionId", SqlDbType.UniqueIdentifier, productionId);
        Add(command, "@Status", SqlDbType.NVarChar, status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordEventAsync<TPayload>(
        EventEnvelope<TPayload> message,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM dbo.ProductionEvents WHERE EventId = @EventId)
            BEGIN
                INSERT INTO dbo.ProductionEvents (
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
            END;
            """;

        Add(command, "@EventId", SqlDbType.UniqueIdentifier, message.EventId);
        Add(command, "@ProductionId", SqlDbType.UniqueIdentifier, message.ProductionId);
        Add(command, "@EventType", SqlDbType.NVarChar, message.EventType);
        Add(command, "@CorrelationId", SqlDbType.UniqueIdentifier, message.CorrelationId);
        Add(command, "@CausationId", SqlDbType.UniqueIdentifier, (object?)message.CausationId ?? DBNull.Value);
        Add(command, "@Producer", SqlDbType.NVarChar, message.Producer);
        Add(command, "@SchemaVersion", SqlDbType.NVarChar, message.SchemaVersion);
        Add(command, "@CreatedAt", SqlDbType.DateTimeOffset, message.CreatedAt);
        Add(command, "@PayloadJson", SqlDbType.NVarChar, JsonSerializer.Serialize(message.Payload));

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
            INSERT INTO dbo.Artifacts (
                ArtifactId,
                ProductionId,
                AgentRole,
                ArtifactUri,
                MediaType,
                Description,
                CreatedAt)
            VALUES (
                NEWID(),
                @ProductionId,
                @AgentRole,
                @ArtifactUri,
                @MediaType,
                @Description,
                SYSUTCDATETIME());
            """;

        Add(command, "@ProductionId", SqlDbType.UniqueIdentifier, productionId);
        Add(command, "@AgentRole", SqlDbType.NVarChar, agentRole);
        Add(command, "@ArtifactUri", SqlDbType.NVarChar, artifact.Uri);
        Add(command, "@MediaType", SqlDbType.NVarChar, artifact.MediaType);
        Add(command, "@Description", SqlDbType.NVarChar, (object?)artifact.Description ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAgentRunAsync(
        AgentRunRecord record,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO dbo.AgentRuns (
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

        Add(command, "@AgentRunId", SqlDbType.UniqueIdentifier, record.AgentRunId);
        Add(command, "@ProductionId", SqlDbType.UniqueIdentifier, record.ProductionId);
        Add(command, "@AgentRole", SqlDbType.NVarChar, record.AgentRole);
        Add(command, "@ConsumedEventId", SqlDbType.UniqueIdentifier, record.ConsumedEventId);
        Add(command, "@PublishedEventId", SqlDbType.UniqueIdentifier, record.PublishedEventId);
        Add(command, "@InputArtifactUri", SqlDbType.NVarChar, (object?)record.InputArtifactUri ?? DBNull.Value);
        Add(command, "@OutputArtifactUri", SqlDbType.NVarChar, (object?)record.OutputArtifactUri ?? DBNull.Value);
        Add(command, "@Status", SqlDbType.NVarChar, record.Status);
        Add(command, "@StartedAt", SqlDbType.DateTimeOffset, record.StartedAt);
        Add(command, "@CompletedAt", SqlDbType.DateTimeOffset, record.CompletedAt);
        Add(command, "@Notes", SqlDbType.NVarChar, (object?)record.Notes ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var connection = new SqlConnection(_connectionString);

            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (SqlException ex) when (attempt < maxAttempts && IsTransientStartupError(ex))
            {
                await connection.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(attempt * 5), cancellationToken);
            }
        }

        var finalConnection = new SqlConnection(_connectionString);
        await finalConnection.OpenAsync(cancellationToken);

        return finalConnection;
    }

    private static bool IsTransientStartupError(SqlException exception)
    {
        foreach (SqlError error in exception.Errors)
        {
            if (error.Number is 40613 or 40197 or 40501 or 4060)
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value;
    }
}
