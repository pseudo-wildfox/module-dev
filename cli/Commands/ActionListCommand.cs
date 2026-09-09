using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Commands;

public sealed class ActionListCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public ActionListCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _database.OpenPublisherConnectionAsync(
                cancellationToken);

        const string sql = """
            SELECT
                module,
                action,
                version,
                http_method,
                target_schema,
                target_function,
                outcomes,
                required_policy,
                idempotency_mode,
                idempotency_scope,
                timeout_ms,
                enabled,
                is_default
            FROM runtime.action_definitions
            ORDER BY module, action, version;
            """;

        await using var command = new NpgsqlCommand(
            sql,
            connection);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var actions = new List<object>();

        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(new
            {
                module = reader.GetString(0),
                action = reader.GetString(1),
                version = reader.GetInt32(2),
                httpMethod = reader.GetString(3),
                targetSchema = reader.GetString(4),
                targetFunction = reader.GetString(5),
                outcomes = reader.GetFieldValue<System.Text.Json.JsonDocument>(6).RootElement.Clone(),
                requiredPolicy = reader.GetFieldValue<System.Text.Json.JsonDocument>(7).RootElement.Clone(),
                idempotencyMode = reader.GetString(8),
                idempotencyScope = reader.GetString(9),
                timeoutMs = reader.GetInt32(10),
                enabled = reader.GetBoolean(11),
                isDefault = reader.GetBoolean(12)
            });
        }

        return _output.Success(new
        {
            resource = "actions",
            count = actions.Count,
            items = actions
        });
    }
}