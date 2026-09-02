using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Commands;

public sealed class FlowGetCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public FlowGetCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var process =
                await GetProcessAsync(
                    connection,
                    transaction,
                    processId,
                    cancellationToken);

            if (process is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "process.not_found",
                    message: "workflow process not found");
            }

            var steps =
                await GetStepsAsync(
                    connection,
                    transaction,
                    processId,
                    cancellationToken);

            var jobs =
                await GetJobsAsync(
                    connection,
                    transaction,
                    processId,
                    cancellationToken);

            var attempts =
                await GetAttemptsAsync(
                    connection,
                    transaction,
                    processId,
                    cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "process",
                process,
                steps,
                jobs,
                attempts
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure.
            }

            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "internal.error",
                message: "failed to get workflow process");
        }
    }

    private static async Task<ProcessResult?> GetProcessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                process_id,
                business_key,
                flow_name,
                flow_version,
                state,
                current_step_key,
                created_at,
                updated_at
            FROM workflow.process_instance
            WHERE process_id = @process_id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new ProcessResult(
            ProcessId: reader.GetGuid(0),
            BusinessKey: reader.GetString(1),
            FlowName: reader.GetString(2),
            FlowVersion: reader.GetInt32(3),
            State: reader.GetString(4),
            CurrentStepKey:
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5),
            CreatedAt:
                reader.GetFieldValue<DateTimeOffset>(6),
            UpdatedAt:
                reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static async Task<IReadOnlyList<StepResult>> GetStepsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                step_instance_id,
                process_id,
                step_key,
                step_type,
                state,
                outcome,
                entered_at,
                completed_at
            FROM workflow.step_instance
            WHERE process_id = @process_id
            ORDER BY
                entered_at,
                step_instance_id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var result = new List<StepResult>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new StepResult(
                    StepInstanceId: reader.GetGuid(0),
                    ProcessId: reader.GetGuid(1),
                    StepKey: reader.GetString(2),
                    StepType: reader.GetString(3),
                    State: reader.GetString(4),
                    Outcome:
                        reader.IsDBNull(5)
                            ? null
                            : reader.GetString(5),
                    EnteredAt:
                        reader.GetFieldValue<DateTimeOffset>(6),
                    CompletedAt:
                        reader.IsDBNull(7)
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<JobResult>> GetJobsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                job_id,
                process_id,
                step_instance_id,
                execution_id,
                state,
                lease_owner,
                lease_version,
                lease_until,
                attempt_count,
                next_attempt_at,
                created_at,
                updated_at
            FROM workflow.workflow_job
            WHERE process_id = @process_id
            ORDER BY
                created_at,
                job_id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var result = new List<JobResult>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new JobResult(
                    JobId: reader.GetGuid(0),
                    ProcessId: reader.GetGuid(1),
                    StepInstanceId: reader.GetGuid(2),
                    ExecutionId: reader.GetGuid(3),
                    State: reader.GetString(4),
                    LeaseOwner:
                        reader.IsDBNull(5)
                            ? null
                            : reader.GetString(5),
                    LeaseVersion: reader.GetInt64(6),
                    LeaseUntil:
                        reader.IsDBNull(7)
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(7),
                    AttemptCount: reader.GetInt32(8),
                    NextAttemptAt:
                        reader.IsDBNull(9)
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(9),
                    CreatedAt:
                        reader.GetFieldValue<DateTimeOffset>(10),
                    UpdatedAt:
                        reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AttemptResult>> GetAttemptsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                a.attempt_id,
                a.job_id,
                a.execution_id,
                a.lease_version,
                a.attempt_number,
                a.status,
                a.outcome,
                a.error_code,
                a.started_at,
                a.finished_at
            FROM workflow.task_attempt AS a
            INNER JOIN workflow.workflow_job AS j
                ON j.job_id = a.job_id
            WHERE j.process_id = @process_id
            ORDER BY
                a.started_at,
                a.attempt_id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var result = new List<AttemptResult>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new AttemptResult(
                    AttemptId: reader.GetGuid(0),
                    JobId: reader.GetGuid(1),
                    ExecutionId: reader.GetGuid(2),
                    LeaseVersion: reader.GetInt64(3),
                    AttemptNumber: reader.GetInt32(4),
                    Status: reader.GetString(5),
                    Outcome:
                        reader.IsDBNull(6)
                            ? null
                            : reader.GetString(6),
                    ErrorCode:
                        reader.IsDBNull(7)
                            ? null
                            : reader.GetString(7),
                    StartedAt:
                        reader.GetFieldValue<DateTimeOffset>(8),
                    FinishedAt:
                        reader.IsDBNull(9)
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return result;
    }

    private sealed record ProcessResult(
        Guid ProcessId,
        string BusinessKey,
        string FlowName,
        int FlowVersion,
        string State,
        string? CurrentStepKey,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record StepResult(
        Guid StepInstanceId,
        Guid ProcessId,
        string StepKey,
        string StepType,
        string State,
        string? Outcome,
        DateTimeOffset EnteredAt,
        DateTimeOffset? CompletedAt);

    private sealed record JobResult(
        Guid JobId,
        Guid ProcessId,
        Guid StepInstanceId,
        Guid ExecutionId,
        string State,
        string? LeaseOwner,
        long LeaseVersion,
        DateTimeOffset? LeaseUntil,
        int AttemptCount,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record AttemptResult(
        Guid AttemptId,
        Guid JobId,
        Guid ExecutionId,
        long LeaseVersion,
        int AttemptNumber,
        string Status,
        string? Outcome,
        string? ErrorCode,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt);
}