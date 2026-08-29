using Npgsql;

namespace Api.Tests.Helpers;

public sealed class AutocheckRepository
{
    private readonly string _connectionString;

    public AutocheckRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<OperationRow>> GetOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                operation_id,
                request_id,
                operation_kind,
                amount,
                currency,
                status,
                process_id
            FROM autocheck.operations
            ORDER BY created_at;
            """;

        var result = new List<OperationRow>();

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new OperationRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetGuid(6)));
        }

        return result;
    }

    public async Task<IReadOnlyList<OperationEventRow>> GetOperationEventsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                event_id,
                operation_id,
                event_type,
                payload_hash
            FROM autocheck.operation_events
            ORDER BY occurred_at;
            """;

        var result = new List<OperationEventRow>();

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new OperationEventRow(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3)));
        }

        return result;
    }

    public async Task<int> CountOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT count(*)
            FROM autocheck.operations;
            """;

        return await ExecuteCountAsync(
            sql,
            cancellationToken);
    }

    public async Task<int> CountOperationEventsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT count(*)
            FROM autocheck.operation_events;
            """;

        return await ExecuteCountAsync(
            sql,
            cancellationToken);
    }

    private async Task<int> ExecuteCountAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        var value =
            await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(value);
    }
}

public sealed record OperationRow(
    Guid OperationId,
    string RequestId,
    string OperationKind,
    decimal Amount,
    string Currency,
    string Status,
    Guid? ProcessId);

public sealed record OperationEventRow(
    Guid EventId,
    Guid OperationId,
    string EventType,
    string PayloadHash);