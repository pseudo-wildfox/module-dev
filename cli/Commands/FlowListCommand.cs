using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Commands;

public sealed class FlowListCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public FlowListCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                fd.flow_name,
                fv.version,
                fv.status,
                fv.is_active,
                fv.published_at
            FROM workflow.flow_definition AS fd
            INNER JOIN workflow.flow_version AS fv
                ON fv.flow_definition_id = fd.id
            ORDER BY
                fd.flow_name,
                fv.version;
            """;

        try
        {
            await using var connection =
                await _database.OpenConnectionAsync(
                    cancellationToken);

            await using var command =
                new NpgsqlCommand(
                    sql,
                    connection);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            var items = new List<object>();

            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new
                {
                    flowName = reader.GetString(0),
                    flowVersion = reader.GetInt32(1),
                    status = reader.GetString(2),
                    isActive = reader.GetBoolean(3),
                    publishedAt = reader.GetFieldValue<DateTimeOffset>(4)
                });
            }

            return _output.Success(new
            {
                items
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "internal.error",
                message: "failed to list workflow flows");
        }
    }
}