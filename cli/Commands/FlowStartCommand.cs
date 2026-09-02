using System.Text.Json;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.Cli.Commands;

public sealed class FlowStartCommand
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public FlowStartCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string flowName,
        string businessKey,
        string? dataPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(flowName))
        {
            return _output.Error(
                code: "request.invalid",
                message: "flow name is required");
        }

        if (string.IsNullOrWhiteSpace(businessKey))
        {
            return _output.Error(
                code: "request.invalid",
                message: "business key is required");
        }

        var dataResult =
            await LoadDataAsync(
                dataPath,
                cancellationToken);

        if (!dataResult.IsValid)
        {
            return _output.Error(
                code: "request.invalid",
                message: dataResult.ErrorMessage!);
        }

        var data = dataResult.Data;

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var flow =
                await ResolveActiveFlowAsync(
                    connection,
                    transaction,
                    flowName,
                    cancellationToken);

            if (flow is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "flow.not_active",
                    message: "workflow has no active version");
            }

            var existingProcess =
                await FindExistingProcessAsync(
                    connection,
                    transaction,
                    flowName,
                    businessKey,
                    data,
                    cancellationToken);

            if (existingProcess is not null)
            {
                if (!existingProcess.DataMatches)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return _output.Error(
                        code: "process.conflict",
                        message:
                            "business key already belongs to a different process");
                }

                await transaction.CommitAsync(
                    cancellationToken);

                return _output.Success(new
                {
                    resource = "process",
                    operation = "started",
                    processId = existingProcess.ProcessId,
                    flowName = existingProcess.FlowName,
                    flowVersion = existingProcess.FlowVersion,
                    state = existingProcess.State,
                    currentStepKey = existingProcess.CurrentStepKey
                });
            }

            var startStep =
                await GetStartStepAsync(
                    connection,
                    transaction,
                    flow.FlowVersionId,
                    flow.StartStep,
                    cancellationToken);

            if (startStep is null)
            {
                throw new InvalidOperationException(
                    "start step could not be resolved");
            }

            var processId = Guid.NewGuid();

            var processState =
                GetInitialProcessState(
                    startStep.StepType);

            await InsertProcessAsync(
                connection,
                transaction,
                processId,
                flow,
                businessKey,
                data,
                processState,
                startStep.StepKey,
                cancellationToken);

            var stepInstanceId = Guid.NewGuid();

            var stepState =
                GetInitialStepState(
                    startStep.StepType);

            var stepOutcome =
                startStep.StepType == "END"
                    ? startStep.StepOutcome
                    : null;

            await InsertStepInstanceAsync(
                connection,
                transaction,
                stepInstanceId,
                processId,
                startStep,
                stepState,
                stepOutcome,
                cancellationToken);

            if (startStep.StepType == "AUTOMATIC")
            {
                await InsertJobAsync(
                    connection,
                    transaction,
                    Guid.NewGuid(),
                    processId,
                    stepInstanceId,
                    Guid.NewGuid(),
                    cancellationToken);
            }

            await InsertProcessStartedEventAsync(
                connection,
                transaction,
                processId,
                stepInstanceId,
                cancellationToken);

            if (startStep.StepType == "AUTOMATIC")
            {
                await InsertStepReadyEventAsync(
                    connection,
                    transaction,
                    processId,
                    stepInstanceId,
                    cancellationToken);
            }
            else if (startStep.StepType == "END")
            {
                await InsertStepCompletedEventAsync(
                    connection,
                    transaction,
                    processId,
                    stepInstanceId,
                    startStep.StepOutcome!,
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "process",
                operation = "started",
                processId,
                flowName = flow.FlowName,
                flowVersion = flow.FlowVersion,
                state = processState,
                currentStepKey = startStep.StepKey
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
                message: "failed to start workflow process");
        }
    }

    private static async Task<DataLoadResult> LoadDataAsync(
        string? dataPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            using var emptyDocument =
                JsonDocument.Parse("{}");

            return DataLoadResult.Success(
                emptyDocument.RootElement.Clone());
        }

        if (!File.Exists(dataPath))
        {
            return DataLoadResult.Failure(
                "data file not found");
        }

        string json;

        try
        {
            json = await File.ReadAllTextAsync(
                dataPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return DataLoadResult.Failure(
                "failed to read data file");
        }

        try
        {
            using var document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return DataLoadResult.Failure(
                    "process data must be a JSON object");
            }

            return DataLoadResult.Success(
                document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return DataLoadResult.Failure(
                "data file contains invalid JSON");
        }
    }

    private static async Task<ActiveFlow?> ResolveActiveFlowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string flowName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                fd.id,
                fd.flow_name,
                fv.id,
                fv.version,
                fv.start_step
            FROM workflow.flow_definition AS fd
            INNER JOIN workflow.flow_version AS fv
                ON fv.flow_definition_id = fd.id
            WHERE fd.flow_name = @flow_name
              AND fv.is_active = true
              AND fv.status = 'PUBLISHED'
            FOR UPDATE OF fd, fv;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "flow_name",
            flowName);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new ActiveFlow(
            FlowDefinitionId: reader.GetInt64(0),
            FlowName: reader.GetString(1),
            FlowVersionId: reader.GetInt64(2),
            FlowVersion: reader.GetInt32(3),
            StartStep: reader.GetString(4));
    }

    private static async Task<ExistingProcess?> FindExistingProcessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string flowName,
        string businessKey,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                process_id,
                flow_name,
                flow_version,
                state,
                current_step_key,
                data = @data
            FROM workflow.process_instance
            WHERE flow_name = @flow_name
              AND business_key = @business_key
            FOR UPDATE;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "flow_name",
            flowName);

        command.Parameters.AddWithValue(
            "business_key",
            businessKey);

        AddJsonbParameter(
            command,
            "data",
            data);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new ExistingProcess(
            ProcessId: reader.GetGuid(0),
            FlowName: reader.GetString(1),
            FlowVersion: reader.GetInt32(2),
            State: reader.GetString(3),
            CurrentStepKey:
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),
            DataMatches: reader.GetBoolean(5));
    }

    private static async Task<StartStep?> GetStartStepAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        string startStep,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                step_key,
                step_type,
                step_outcome
            FROM workflow.step_definition
            WHERE flow_version_id = @flow_version_id
              AND step_key = @step_key;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "flow_version_id",
            flowVersionId);

        command.Parameters.AddWithValue(
            "step_key",
            startStep);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new StartStep(
            StepKey: reader.GetString(0),
            StepType: reader.GetString(1),
            StepOutcome:
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2));
    }

    private static async Task InsertProcessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        ActiveFlow flow,
        string businessKey,
        JsonElement data,
        string state,
        string currentStepKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.process_instance
            (
                process_id,
                flow_definition_id,
                flow_version_id,
                flow_name,
                flow_version,
                business_key,
                data,
                state,
                current_step_key
            )
            VALUES
            (
                @process_id,
                @flow_definition_id,
                @flow_version_id,
                @flow_name,
                @flow_version,
                @business_key,
                @data,
                @state,
                @current_step_key
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "flow_definition_id",
            flow.FlowDefinitionId);

        command.Parameters.AddWithValue(
            "flow_version_id",
            flow.FlowVersionId);

        command.Parameters.AddWithValue(
            "flow_name",
            flow.FlowName);

        command.Parameters.AddWithValue(
            "flow_version",
            flow.FlowVersion);

        command.Parameters.AddWithValue(
            "business_key",
            businessKey);

        AddJsonbParameter(
            command,
            "data",
            data);

        command.Parameters.AddWithValue(
            "state",
            state);

        command.Parameters.AddWithValue(
            "current_step_key",
            currentStepKey);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertStepInstanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid stepInstanceId,
        Guid processId,
        StartStep step,
        string state,
        string? outcome,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.step_instance
            (
                step_instance_id,
                process_id,
                step_key,
                step_type,
                state,
                outcome,
                entered_at,
                completed_at
            )
            VALUES
            (
                @step_instance_id,
                @process_id,
                @step_key,
                @step_type,
                @state,
                @outcome,
                now(),
                CASE
                    WHEN @state IN ('COMPLETED', 'FAILED')
                    THEN now()
                    ELSE NULL
                END
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "step_instance_id",
            stepInstanceId);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "step_key",
            step.StepKey);

        command.Parameters.AddWithValue(
            "step_type",
            step.StepType);

        command.Parameters.AddWithValue(
            "state",
            state);

        command.Parameters.AddWithValue(
            "outcome",
            (object?)outcome ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid jobId,
        Guid processId,
        Guid stepInstanceId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.workflow_job
            (
                job_id,
                process_id,
                step_instance_id,
                execution_id,
                state,
                lease_owner,
                lease_version,
                lease_until,
                attempt_count,
                next_attempt_at
            )
            VALUES
            (
                @job_id,
                @process_id,
                @step_instance_id,
                @execution_id,
                'READY',
                NULL,
                0,
                NULL,
                0,
                NULL
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "job_id",
            jobId);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "step_instance_id",
            stepInstanceId);

        command.Parameters.AddWithValue(
            "execution_id",
            executionId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertProcessStartedEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        Guid stepInstanceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.workflow_event
            (
                event_id,
                process_id,
                step_instance_id,
                event_type,
                payload
            )
            VALUES
            (
                @event_id,
                @process_id,
                @step_instance_id,
                'ProcessStarted',
                '{}'::jsonb
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "event_id",
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "step_instance_id",
            stepInstanceId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertStepReadyEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        Guid stepInstanceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.workflow_event
            (
                event_id,
                process_id,
                step_instance_id,
                event_type,
                payload
            )
            VALUES
            (
                @event_id,
                @process_id,
                @step_instance_id,
                'StepReady',
                '{}'::jsonb
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "event_id",
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "step_instance_id",
            stepInstanceId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertStepCompletedEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        Guid stepInstanceId,
        string outcome,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.workflow_event
            (
                event_id,
                process_id,
                step_instance_id,
                event_type,
                payload
            )
            VALUES
            (
                @event_id,
                @process_id,
                @step_instance_id,
                'StepCompleted',
                @payload
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "event_id",
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "step_instance_id",
            stepInstanceId);

        var payloadParameter =
            command.Parameters.Add(
                "payload",
                NpgsqlDbType.Jsonb);

        payloadParameter.Value =
            JsonSerializer.Serialize(
                new
                {
                    outcome
                });

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string GetInitialProcessState(
        string stepType)
    {
        return stepType switch
        {
            "AUTOMATIC" => "RUNNING",
            "WAIT_SIGNAL" => "WAITING_SIGNAL",
            "MANUAL" => "WAITING_MANUAL",
            "END" => "COMPLETED",
            _ => throw new InvalidOperationException(
                $"unsupported start step type: {stepType}")
        };
    }

    private static string GetInitialStepState(
        string stepType)
    {
        return stepType switch
        {
            "AUTOMATIC" => "READY",
            "WAIT_SIGNAL" => "WAITING",
            "MANUAL" => "WAITING",
            "END" => "COMPLETED",
            _ => throw new InvalidOperationException(
                $"unsupported start step type: {stepType}")
        };
    }

    private static void AddJsonbParameter(
        NpgsqlCommand command,
        string name,
        JsonElement value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                NpgsqlDbType.Jsonb);

        parameter.Value =
            value.GetRawText();
    }

    private sealed record ActiveFlow(
        long FlowDefinitionId,
        string FlowName,
        long FlowVersionId,
        int FlowVersion,
        string StartStep);

    private sealed record StartStep(
        string StepKey,
        string StepType,
        string? StepOutcome);

    private sealed record ExistingProcess(
        Guid ProcessId,
        string FlowName,
        int FlowVersion,
        string State,
        string? CurrentStepKey,
        bool DataMatches);

    private sealed record DataLoadResult(
        bool IsValid,
        JsonElement Data,
        string? ErrorMessage)
    {
        public static DataLoadResult Success(
            JsonElement data)
        {
            return new DataLoadResult(
                true,
                data,
                null);
        }

        public static DataLoadResult Failure(
            string errorMessage)
        {
            return new DataLoadResult(
                false,
                default,
                errorMessage);
        }
    }
}