using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ModuleDev.Api.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly Database.DbConnectionFactory _connectionFactory;

    public DatabaseHealthCheck(
        Database.DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection =
                await _connectionFactory.OpenAsync(cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandText = "SELECT 1;";
            command.CommandTimeout = 3;

            var result =
                await command.ExecuteScalarAsync(cancellationToken);

            if (result is 1)
            {
                return HealthCheckResult.Healthy(
                    "PostgreSQL is available.");
            }

            return HealthCheckResult.Unhealthy(
                "PostgreSQL health probe returned an unexpected result.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL health probe was cancelled.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL is unavailable.");
        }
    }
}