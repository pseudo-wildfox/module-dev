using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Commands;

public sealed class FlowActivateCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public FlowActivateCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string flowName,
        int version,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _database.OpenPublisherConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            const string lockFlowSql = """
                SELECT id
                FROM workflow.flow_definition
                WHERE flow_name = @flow_name;
                """;

            long flowDefinitionId;

            await using (var command = new NpgsqlCommand(
                lockFlowSql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "flow_name",
                    flowName);

                var result =
                    await command.ExecuteScalarAsync(
                        cancellationToken);

                if (result is null)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return _output.Error(
                        code: "flow.not_found",
                        message: "workflow flow not found");
                }

                flowDefinitionId = Convert.ToInt64(result);
            }

            const string verifyVersionSql = """
                SELECT id
                FROM workflow.flow_version
                WHERE flow_definition_id = @flow_definition_id
                AND version = @version
                AND status = 'PUBLISHED';
                """;

            long flowVersionId;

            await using (var command = new NpgsqlCommand(
                verifyVersionSql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "flow_definition_id",
                    flowDefinitionId);

                command.Parameters.AddWithValue(
                    "version",
                    version);

                var result =
                    await command.ExecuteScalarAsync(
                        cancellationToken);

                if (result is null)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return _output.Error(
                        code: "flow.version_not_found",
                        message: "published workflow version not found");
                }

                flowVersionId = Convert.ToInt64(result);
            }

            const string deactivateSql = """
                UPDATE workflow.flow_version
                SET is_active = false
                WHERE flow_definition_id = @flow_definition_id
                  AND is_active = true;
                """;

            await using (var command = new NpgsqlCommand(
                deactivateSql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "flow_definition_id",
                    flowDefinitionId);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            const string activateSql = """
                UPDATE workflow.flow_version
                SET is_active = true
                WHERE id = @flow_version_id;
                """;

            await using (var command = new NpgsqlCommand(
                activateSql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "flow_version_id",
                    flowVersionId);

                var affected =
                    await command.ExecuteNonQueryAsync(
                        cancellationToken);

                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        "workflow version activation failed");
                }
            }

            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "flow",
                operation = "activated",
                flowName,
                flowVersion = version
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PostgresException ex)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "internal.error",
                message: "workflow activation failed");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "internal.error",
                message: "workflow activation failed");
        }
    }
}