using Npgsql;

namespace ModuleDev.Cli.Database;

public sealed class CliDatabase
{
    public Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        return OpenConnectionAsync(
            usernameVariable: "DB_USER",
            passwordVariable: "DB_PASSWORD",
            cancellationToken);
    }

    public async Task<NpgsqlConnection> OpenPublisherConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        return await OpenConnectionAsync(
            usernameVariable: "DB_PUBLISHER_USER",
            passwordVariable: "DB_PUBLISHER_PASSWORD",
            cancellationToken);
    }

    public async Task<NpgsqlConnection> OpenOwnerConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenConnectionAsync(
            cancellationToken);

        try
        {
            await SetOwnerRoleAsync(
                connection,
                cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task SetOwnerRoleAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET ROLE course_owner;
            """;

        await using var command = new NpgsqlCommand(
            sql,
            connection);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(
        string usernameVariable,
        string passwordVariable,
        CancellationToken cancellationToken)
    {
        var host = GetRequiredEnvironmentVariable("DB_HOST");

        var port = GetEnvironmentVariable(
            "DB_PORT",
            "5432");

        var database = GetRequiredEnvironmentVariable("DB_NAME");

        var username = GetRequiredEnvironmentVariable(
            usernameVariable);

        var password = GetRequiredEnvironmentVariable(
            passwordVariable);

        if (!int.TryParse(
                port,
                out var portNumber))
        {
            throw new InvalidOperationException(
                $"environment variable 'DB_PORT' has invalid value: '{port}'");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = portNumber,
            Database = database,
            Username = username,
            Password = password,
            ApplicationName = "moduledev-cli",
            Timeout = 10,
            CommandTimeout = 30,
            Pooling = false
        };

        var connection = new NpgsqlConnection(
            builder.ConnectionString);

        try
        {
            await connection.OpenAsync(
                cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string GetRequiredEnvironmentVariable(
        string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"environment variable '{name}' is required");
        }

        return value;
    }

    private static string GetEnvironmentVariable(
        string name,
        string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }
}