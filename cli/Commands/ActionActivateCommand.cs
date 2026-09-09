using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Commands;

public sealed class ActionActivateCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public ActionActivateCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string routeKey,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRouteKey(
                routeKey,
                out var module,
                out var action))
        {
            return _output.Error(
                code: "request.invalid",
                message: "route must have format <module.action>");
        }

        if (version < 1)
        {
            return _output.Error(
                code: "request.invalid",
                message: "version must be greater than zero");
        }

        await using var connection =
            await _database.OpenPublisherConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var versions =
                await LoadVersionsAsync(
                    connection,
                    transaction,
                    module,
                    action,
                    cancellationToken);

            if (versions.Count == 0)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "action.not_found",
                    message: "action route is not published");
            }

            var selected = versions
                .FirstOrDefault(x => x.Version == version);

            if (selected is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "action.not_found",
                    message: "action version is not published");
            }

            // Remove default from every version of this action.
            //
            // We do NOT disable them.
            const string clearDefaultSql = """
                UPDATE runtime.action_definitions
                SET is_default = false
                WHERE module = @module
                  AND action = @action
                  AND is_default = true;
                """;

            await using (var clearDefaultCommand =
                new NpgsqlCommand(
                    clearDefaultSql,
                    connection,
                    transaction))
            {
                clearDefaultCommand.Parameters.AddWithValue(
                    "module",
                    module);

                clearDefaultCommand.Parameters.AddWithValue(
                    "action",
                    action);

                await clearDefaultCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            // Activate and make the selected version default.
            const string activateSql = """
                UPDATE runtime.action_definitions
                SET enabled = true,
                    is_default = true
                WHERE module = @module
                  AND action = @action
                  AND version = @version;
                """;

            await using (var activateCommand =
                new NpgsqlCommand(
                    activateSql,
                    connection,
                    transaction))
            {
                activateCommand.Parameters.AddWithValue(
                    "module",
                    module);

                activateCommand.Parameters.AddWithValue(
                    "action",
                    action);

                activateCommand.Parameters.AddWithValue(
                    "version",
                    version);

                var affected =
                    await activateCommand.ExecuteNonQueryAsync(
                        cancellationToken);

                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        "activation update affected unexpected number of rows");
                }
            }

            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "action",
                operation = "activated",
                key = routeKey,
                version
            });
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    private static async Task<List<ActionVersionState>> LoadVersionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string module,
        string action,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                version,
                enabled,
                is_default
            FROM runtime.action_definitions
            WHERE module = @module
              AND action = @action
            ORDER BY version
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(
            sql,
            connection,
            transaction);

        command.Parameters.AddWithValue(
            "module",
            module);

        command.Parameters.AddWithValue(
            "action",
            action);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var result = new List<ActionVersionState>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(new ActionVersionState(
                Version: reader.GetInt32(0),
                Enabled: reader.GetBoolean(1),
                IsDefault: reader.GetBoolean(2)));
        }

        return result;
    }

    private static bool TryParseRouteKey(
        string value,
        out string module,
        out string action)
    {
        module = string.Empty;
        action = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex =
            value.IndexOf('.');

        if (separatorIndex <= 0 ||
            separatorIndex == value.Length - 1 ||
            value.IndexOf(
                '.',
                separatorIndex + 1) >= 0)
        {
            return false;
        }

        module = value[..separatorIndex];
        action = value[(separatorIndex + 1)..];

        return true;
    }

    private sealed record ActionVersionState(
        int Version,
        bool Enabled,
        bool IsDefault);
}