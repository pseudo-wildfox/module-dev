using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Commands;

public sealed class ActionDisableCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public ActionDisableCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string actionKey,
        int version,
        int? replacementVersion,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseActionKey(
                actionKey,
                out var module,
                out var action))
        {
            return _output.Error(
                code: "request.invalid",
                message: "action key must have format <module.action>");
        }

        if (version < 1)
        {
            return _output.Error(
                code: "request.invalid",
                message: "version must be greater than or equal to 1");
        }

        if (replacementVersion is not null
            && replacementVersion.Value < 1)
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "replacement version must be greater than or equal to 1");
        }

        if (replacementVersion == version)
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "replacement version must differ from version");
        }

        await using var connection =
            await _database.OpenPublisherConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var versions = await LoadRouteAsync(
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
                    message: "action is not published");
            }

            var selected = versions.FirstOrDefault(
                x => x.Version == version);

            if (selected is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "action.not_found",
                    message: "action version is not published");
            }

            if (!selected.IsEnabled)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "action.invalid_state",
                    message: "action version is already disabled");
            }

            if (!selected.IsDefault)
            {
                await DisableVersionAsync(
                    connection,
                    transaction,
                    module,
                    action,
                    version,
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                return _output.Success(new
                {
                    resource = "action",
                    operation = "disabled",
                    key = actionKey,
                    version
                });
            }

            // Selected version is the default.
            // A replacement is mandatory.
            if (replacementVersion is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "action.replacement_required",
                    message:
                        "default action version requires a replacement version");
            }

            var replacement = versions.FirstOrDefault(
                x => x.Version == replacementVersion.Value);

            if (replacement is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "action.not_found",
                    message:
                        "replacement action version is not published");
            }

            // Make replacement the only default.
            await ClearDefaultsAsync(
                connection,
                transaction,
                module,
                action,
                cancellationToken);

            await EnableAndSetDefaultAsync(
                connection,
                transaction,
                module,
                action,
                replacement.Version,
                cancellationToken);

            await DisableVersionAsync(
                connection,
                transaction,
                module,
                action,
                version,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "action",
                operation = "disabled",
                key = actionKey,
                version,
                replacementVersion = replacement.Version
            });
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    private static async Task<List<ActionVersionState>> LoadRouteAsync(
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

        var result = new List<ActionVersionState>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
            cancellationToken))
        {
            result.Add(
                new ActionVersionState(
                    Version: reader.GetInt32(0),
                    IsEnabled: reader.GetBoolean(1),
                    IsDefault: reader.GetBoolean(2)));
        }

        return result;
    }

    private static async Task ClearDefaultsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string module,
        string action,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE runtime.action_definitions
            SET is_default = false
            WHERE module = @module
              AND action = @action;
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

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task EnableAndSetDefaultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string module,
        string action,
        int version,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE runtime.action_definitions
            SET
                enabled = true,
                is_default = true
            WHERE module = @module
              AND action = @action
              AND version = @version;
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

        command.Parameters.AddWithValue(
            "version",
            version);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task DisableVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string module,
        string action,
        int version,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE runtime.action_definitions
            SET
                enabled = false,
                is_default = false
            WHERE module = @module
              AND action = @action
              AND version = @version;
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

        command.Parameters.AddWithValue(
            "version",
            version);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static bool TryParseActionKey(
        string actionKey,
        out string module,
        out string action)
    {
        module = string.Empty;
        action = string.Empty;

        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return false;
        }

        var separator = actionKey.IndexOf('.');

        if (separator <= 0
            || separator != actionKey.LastIndexOf('.')
            || separator == actionKey.Length - 1)
        {
            return false;
        }

        module = actionKey[..separator];
        action = actionKey[(separator + 1)..];

        return IsSqlIdentifier(module)
            && IsSqlIdentifier(action);
    }

    private static bool IsSqlIdentifier(string value)
    {
        if (value.Length is < 1 or > 63)
        {
            return false;
        }

        if (!IsLowercaseLetter(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            var character = value[i];

            if (!IsLowercaseLetter(character)
                && !char.IsAsciiDigit(character)
                && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowercaseLetter(char character)
    {
        return character is >= 'a' and <= 'z';
    }

    private sealed record ActionVersionState(
        int Version,
        bool IsEnabled,
        bool IsDefault);
}