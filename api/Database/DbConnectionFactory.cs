using Npgsql;

namespace ModuleDev.Api.Database;

public sealed class DbConnectionFactory
{
    public async Task<NpgsqlConnection> OpenAsync(
        CancellationToken cancellationToken = default)
    {
        var host = GetRequired("DB_HOST");
        var port = GetOptional("DB_PORT", "5432");
        var database = GetRequired("DB_NAME");
        var username = GetRequired("DB_USER");
        var password = GetRequired("DB_PASSWORD");

        if (!int.TryParse(port, out var portNumber))
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
            ApplicationName = "moduledev-api",
            Timeout = 10,
            CommandTimeout = 30,
            Pooling = true
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string GetRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"environment variable '{name}' is required");
        }

        return value;
    }

    private static string GetOptional(
        string name,
        string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }
}