using System.Text.Json;
using ModuleDev.Contracts;
using Npgsql;

namespace ModuleDev.Api.Database;

public interface IActionRepository
{
    Task<ActionManifest?> GetExplicitAsync(
        string module,
        string action,
        int version,
        CancellationToken cancellationToken = default);

    Task<ActionManifest?> GetDefaultAsync(
        string module,
        string action,
        CancellationToken cancellationToken = default);
}

public sealed class ActionRepository : IActionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ActionRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ActionManifest?> GetExplicitAsync(
        string module,
        string action,
        int version,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                contract_version,
                module,
                action,
                version,
                http_method,
                target_schema,
                target_function,
                request_schema,
                response_schema,
                outcomes,
                required_policy,
                idempotency_mode,
                idempotency_scope,
                timeout_ms,
                enabled,
                is_default
            FROM runtime.action_definitions
            WHERE module = @module
              AND action = @action
              AND version = @version
              AND enabled = true;
            """;

        await using var connection =
            await _connectionFactory.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("module", module);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("version", version);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadManifest(reader);
    }

    public async Task<ActionManifest?> GetDefaultAsync(
        string module,
        string action,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                contract_version,
                module,
                action,
                version,
                http_method,
                target_schema,
                target_function,
                request_schema,
                response_schema,
                outcomes,
                required_policy,
                idempotency_mode,
                idempotency_scope,
                timeout_ms,
                enabled,
                is_default
            FROM runtime.action_definitions
            WHERE module = @module
              AND action = @action
              AND enabled = true
              AND is_default = true;
            """;

        await using var connection =
            await _connectionFactory.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("module", module);
        command.Parameters.AddWithValue("action", action);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadManifest(reader);
    }

    private static ActionManifest ReadManifest(
        NpgsqlDataReader reader)
    {
        return new ActionManifest
        {
            ContractVersion = reader.GetString(0),
            Module = reader.GetString(1),
            Action = reader.GetString(2),
            Version = reader.GetInt32(3),
            HttpMethod = reader.GetString(4),
            TargetSchema = reader.GetString(5),
            TargetFunction = reader.GetString(6),
            RequestSchema = ReadJsonElement(reader, 7),
            ResponseSchema = ReadJsonElement(reader, 8),
            Outcomes = ReadStringArray(reader, 9),
            RequiredPolicy = ReadStringArray(reader, 10),
            IdempotencyMode = reader.GetString(11),
            IdempotencyScope = reader.GetString(12),
            TimeoutMs = reader.GetInt32(13),
            Enabled = reader.GetBoolean(14),
            IsDefault = reader.GetBoolean(15)
        };
    }

    private static JsonElement ReadJsonElement(
        NpgsqlDataReader reader,
        int ordinal)
    {
        var json = reader.GetString(ordinal);

        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> ReadStringArray(
        NpgsqlDataReader reader,
        int ordinal)
    {
        var json = reader.GetString(ordinal);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Expected JSON array in action catalog.");
        }

        return document.RootElement
            .EnumerateArray()
            .Select(element =>
                element.GetString()
                ?? throw new InvalidOperationException(
                    "Expected string value in action catalog JSON array."))
            .ToArray();
    }
}