using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.Cli.Commands;

public sealed class FlowSignalCommand
{
    private const string SignalTypePattern =
        "^[a-z][a-z0-9_-]*(\\.[a-z][a-z0-9_-]*)*$";

    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public FlowSignalCommand(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        Guid processId,
        string signalType,
        string messageId,
        string payloadPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signalType))
        {
            return _output.Error(
                code: "request.invalid",
                message: "signal type is required");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                signalType,
                SignalTypePattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return _output.Error(
                code: "request.invalid",
                message: "signal type has invalid format");
        }

        if (string.IsNullOrWhiteSpace(messageId))
        {
            return _output.Error(
                code: "request.invalid",
                message: "message id is required");
        }

        var payloadResult =
            await LoadPayloadAsync(
                payloadPath,
                cancellationToken);

        if (!payloadResult.IsValid)
        {
            return _output.Error(
                code: "request.invalid",
                message: payloadResult.ErrorMessage!);
        }

        var body = payloadResult.Body;
        var bodyJson = SerializeJson(body);
        var bodyHash = CalculateSha256(bodyJson);

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var process =
                await GetProcessForUpdateAsync(
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

            var signalDeclarationExists =
                await SignalTypeExistsInPinnedFlowAsync(
                    connection,
                    transaction,
                    process.FlowVersionId,
                    signalType,
                    cancellationToken);

            if (!signalDeclarationExists)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return _output.Error(
                    code: "signal.not_allowed",
                    message:
                        "signal type is not declared by the pinned flow version");
            }

            var inserted =
                await TryInsertSignalAsync(
                    connection,
                    transaction,
                    messageId,
                    processId,
                    signalType,
                    bodyHash,
                    body,
                    cancellationToken);

            if (!inserted)
            {
                var existingSignal =
                    await GetExistingSignalAsync(
                        connection,
                        transaction,
                        messageId,
                        cancellationToken);

                if (existingSignal is null)
                {
                    throw new InvalidOperationException(
                        "existing workflow signal could not be resolved");
                }

                var sameSignal =
                    existingSignal.ProcessId == processId
                    &&
                    string.Equals(
                        existingSignal.SignalType,
                        signalType,
                        StringComparison.Ordinal)
                    &&
                    string.Equals(
                        existingSignal.BodyHash,
                        bodyHash,
                        StringComparison.Ordinal);

                if (!sameSignal)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return _output.Error(
                        code: "signal.conflict",
                        message:
                            "message id already belongs to a different signal");
                }

                await transaction.CommitAsync(
                    cancellationToken);

                return _output.Success(new
                {
                    resource = "signal",
                    processId,
                    messageId,
                    signalType,
                    status = "duplicate"
                });
            }

            /*
             * A signal may arrive before the process reaches its
             * corresponding WAIT_SIGNAL step. In that case it remains
             * ACCEPTED and is applied later by workflow transition logic.
             */
            var currentStep =
                await GetCurrentStepAsync(
                    connection,
                    transaction,
                    process.FlowVersionId,
                    process.CurrentStepKey,
                    cancellationToken);

            var waitingForSignal =
                process.State == "WAITING_SIGNAL"
                &&
                currentStep is not null
                &&
                currentStep.StepType == "WAIT_SIGNAL"
                &&
                string.Equals(
                    currentStep.SignalType,
                    signalType,
                    StringComparison.Ordinal);

            if (waitingForSignal)
            {
                if (string.IsNullOrWhiteSpace(
                        currentStep!.StepOutcome))
                {
                    throw new InvalidOperationException(
                        "waiting signal step has no outcome");
                }

                await ApplySignalAsync(
                    connection,
                    transaction,
                    process,
                    currentStep,
                    messageId,
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "signal",
                processId,
                messageId,
                signalType,
                status = "accepted"
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
                // Preserve original failure.
            }

            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "internal.error",
                message: "failed to process workflow signal");
        }
    }

    private static async Task<PayloadLoadResult> LoadPayloadAsync(
        string payloadPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payloadPath))
        {
            return PayloadLoadResult.Failure(
                "payload file is required");
        }

        if (!File.Exists(payloadPath))
        {
            return PayloadLoadResult.Failure(
                "payload file not found");
        }

        string json;

        try
        {
            json = await File.ReadAllTextAsync(
                payloadPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return PayloadLoadResult.Failure(
                "failed to read payload file");
        }

        try
        {
            using var document =
                JsonDocument.Parse(json);

            return PayloadLoadResult.Success(
                document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return PayloadLoadResult.Failure(
                "payload file contains invalid JSON");
        }
    }

    private static async Task<Process?> GetProcessForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                process_id,
                flow_version_id,
                flow_name,
                flow_version,
                business_key,
                state,
                current_step_key
            FROM workflow.process_instance
            WHERE process_id = @process_id
            FOR UPDATE;
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

        return new Process(
            ProcessId: reader.GetGuid(0),
            FlowVersionId: reader.GetInt64(1),
            FlowName: reader.GetString(2),
            FlowVersion: reader.GetInt32(3),
            BusinessKey: reader.GetString(4),
            State: reader.GetString(5),
            CurrentStepKey:
                reader.IsDBNull(6)
                    ? null
                    : reader.GetString(6));
    }

    private static async Task<bool> SignalTypeExistsInPinnedFlowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        string signalType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS
            (
                SELECT 1
                FROM workflow.step_definition
                WHERE flow_version_id = @flow_version_id
                  AND step_type = 'WAIT_SIGNAL'
                  AND signal_type = @signal_type
            );
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
            "signal_type",
            signalType);

        return (bool)(
            await command.ExecuteScalarAsync(
                cancellationToken)
            ?? false);
    }

    private static async Task<bool> TryInsertSignalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string messageId,
        Guid processId,
        string signalType,
        string bodyHash,
        JsonElement body,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.workflow_signal
            (
                message_id,
                process_id,
                signal_type,
                body_hash,
                body,
                status
            )
            VALUES
            (
                @message_id,
                @process_id,
                @signal_type,
                @body_hash,
                @body,
                'ACCEPTED'
            )
            ON CONFLICT (message_id)
            DO NOTHING
            RETURNING message_id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "message_id",
            messageId);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "signal_type",
            signalType);

        command.Parameters.AddWithValue(
            "body_hash",
            bodyHash);

        var bodyParameter =
            command.Parameters.Add(
                "body",
                NpgsqlDbType.Jsonb);

        bodyParameter.Value =
            body.GetRawText();

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is not null;
    }

    private static async Task<ExistingSignal?> GetExistingSignalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string messageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                process_id,
                signal_type,
                body_hash
            FROM workflow.workflow_signal
            WHERE message_id = @message_id
            FOR UPDATE;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "message_id",
            messageId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new ExistingSignal(
            ProcessId: reader.GetGuid(0),
            SignalType: reader.GetString(1),
            BodyHash: reader.GetString(2));
    }

    private static async Task<CurrentStep?> GetCurrentStepAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        string? currentStepKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentStepKey))
        {
            return null;
        }

        const string sql = """
            SELECT
                step_key,
                step_type,
                signal_type,
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
            currentStepKey);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new CurrentStep(
            StepKey: reader.GetString(0),
            StepType: reader.GetString(1),
            SignalType:
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),
            StepOutcome:
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3));
    }

    private static async Task ApplySignalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Process process,
        CurrentStep currentStep,
        string messageId,
        CancellationToken cancellationToken)
    {
        var stepInstance =
            await GetCurrentStepInstanceForUpdateAsync(
                connection,
                transaction,
                process.ProcessId,
                currentStep.StepKey,
                cancellationToken);

        if (stepInstance is null)
        {
            throw new InvalidOperationException(
                "current step instance could not be resolved");
        }

        await CompleteCurrentStepAsync(
            connection,
            transaction,
            stepInstance.StepInstanceId,
            currentStep.StepOutcome!,
            cancellationToken);

        await InsertWorkflowEventAsync(
            connection,
            transaction,
            process.ProcessId,
            stepInstance.StepInstanceId,
            "StepCompleted",
            new
            {
                outcome = currentStep.StepOutcome
            },
            cancellationToken);

        var transition =
            await GetTransitionAsync(
                connection,
                transaction,
                process.FlowVersionId,
                currentStep.StepKey,
                currentStep.StepOutcome!,
                cancellationToken);

        if (transition is null)
        {
            throw new InvalidOperationException(
                "transition for signal outcome could not be resolved");
        }

        var nextStep =
            await GetNextStepAsync(
                connection,
                transaction,
                process.FlowVersionId,
                transition.ToStepKey,
                cancellationToken);

        if (nextStep is null)
        {
            throw new InvalidOperationException(
                "next workflow step could not be resolved");
        }

        var nextStepInstanceId = Guid.NewGuid();

        var nextProcessState =
            GetProcessStateForNextStep(
                nextStep.StepType);

        var nextStepState =
            GetStepStateForNextStep(
                nextStep.StepType);

        var nextStepOutcome =
            nextStep.StepType == "END"
                ? nextStep.StepOutcome
                : null;

        await InsertStepInstanceAsync(
            connection,
            transaction,
            nextStepInstanceId,
            process.ProcessId,
            nextStep,
            nextStepState,
            nextStepOutcome,
            cancellationToken);

        await UpdateProcessAsync(
            connection,
            transaction,
            process.ProcessId,
            nextProcessState,
            nextStep.StepKey,
            cancellationToken);

        if (nextStep.StepType == "AUTOMATIC")
        {
            await InsertJobAsync(
                connection,
                transaction,
                process.ProcessId,
                nextStepInstanceId,
                cancellationToken);

            await InsertWorkflowEventAsync(
                connection,
                transaction,
                process.ProcessId,
                nextStepInstanceId,
                "StepReady",
                null,
                cancellationToken);
        }

        await MarkSignalAppliedAsync(
            connection,
            transaction,
            messageId,
            cancellationToken);

        if (nextStep.StepType == "END")
        {
            await InsertWorkflowEventAsync(
                connection,
                transaction,
                process.ProcessId,
                nextStepInstanceId,
                "StepCompleted",
                new
                {
                    outcome = nextStep.StepOutcome
                },
                cancellationToken);

            await InsertWorkflowEventAsync(
                connection,
                transaction,
                process.ProcessId,
                nextStepInstanceId,
                "ProcessCompleted",
                new
                {
                    outcome = nextStep.StepOutcome
                },
                cancellationToken);
        }
    }

    private static async Task<StepInstance?> GetCurrentStepInstanceForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        string stepKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                step_instance_id
            FROM workflow.step_instance
            WHERE process_id = @process_id
              AND step_key = @step_key
              AND state = 'WAITING'
            FOR UPDATE;
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
            "step_key",
            stepKey);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null
            ? null
            : new StepInstance(
                (Guid)result);
    }

    private static async Task CompleteCurrentStepAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid stepInstanceId,
        string outcome,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE workflow.step_instance
            SET
                state = 'COMPLETED',
                outcome = @outcome,
                completed_at = now()
            WHERE step_instance_id = @step_instance_id
              AND state = 'WAITING';
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
            "outcome",
            outcome);

        var affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "waiting step could not be completed");
        }
    }

    private static async Task<TransitionRecord?> GetTransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        string fromStepKey,
        string outcome,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                to_step_key
            FROM workflow.transition_definition
            WHERE flow_version_id = @flow_version_id
              AND from_step_key = @from_step_key
              AND outcome = @outcome;
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
            "from_step_key",
            fromStepKey);

        command.Parameters.AddWithValue(
            "outcome",
            outcome);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null
            ? null
            : new TransitionRecord(
                result.ToString()!);
    }

    private static async Task<CurrentStep?> GetNextStepAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        string stepKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                step_key,
                step_type,
                signal_type,
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
            stepKey);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new CurrentStep(
            StepKey: reader.GetString(0),
            StepType: reader.GetString(1),
            SignalType:
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),
            StepOutcome:
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3));
    }

    private static async Task InsertStepInstanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid stepInstanceId,
        Guid processId,
        CurrentStep step,
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

    private static async Task UpdateProcessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        string state,
        string currentStepKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE workflow.process_instance
            SET
                state = @state,
                current_step_key = @current_step_key,
                updated_at = now()
            WHERE process_id = @process_id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "state",
            state);

        command.Parameters.AddWithValue(
            "current_step_key",
            currentStepKey);

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        Guid stepInstanceId,
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
            Guid.NewGuid());

        command.Parameters.AddWithValue(
            "process_id",
            processId);

        command.Parameters.AddWithValue(
            "step_instance_id",
            stepInstanceId);

        command.Parameters.AddWithValue(
            "execution_id",
            Guid.NewGuid());

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task MarkSignalAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string messageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE workflow.workflow_signal
            SET status = 'APPLIED'
            WHERE message_id = @message_id
              AND status = 'ACCEPTED';
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "message_id",
            messageId);

        var affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "workflow signal could not be marked as applied");
        }
    }

    private static async Task InsertWorkflowEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processId,
        Guid stepInstanceId,
        string eventType,
        object? payload,
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
                @event_type,
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

        command.Parameters.AddWithValue(
            "event_type",
            eventType);

        var payloadParameter =
            command.Parameters.Add(
                "payload",
                NpgsqlDbType.Jsonb);

        payloadParameter.Value =
            payload is null
                ? "{}"
                : JsonSerializer.Serialize(
                    payload,
                    JsonOptions);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string GetProcessStateForNextStep(
        string stepType)
    {
        return stepType switch
        {
            "AUTOMATIC" => "RUNNING",
            "WAIT_SIGNAL" => "WAITING_SIGNAL",
            "MANUAL" => "WAITING_MANUAL",
            "END" => "COMPLETED",
            _ => throw new InvalidOperationException(
                $"unsupported workflow step type: {stepType}")
        };
    }

    private static string GetStepStateForNextStep(
        string stepType)
    {
        return stepType switch
        {
            "AUTOMATIC" => "READY",
            "WAIT_SIGNAL" => "WAITING",
            "MANUAL" => "WAITING",
            "END" => "COMPLETED",
            _ => throw new InvalidOperationException(
                $"unsupported workflow step type: {stepType}")
        };
    }

    private static string SerializeJson(
        JsonElement body)
    {
        return JsonSerializer.Serialize(
            body,
            JsonOptions);
    }

    private static string CalculateSha256(
        string value)
    {
        var bytes =
            Encoding.UTF8.GetBytes(value);

        return Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    private sealed record PayloadLoadResult(
        bool IsValid,
        JsonElement Body,
        string? ErrorMessage)
    {
        public static PayloadLoadResult Success(
            JsonElement body)
        {
            return new PayloadLoadResult(
                true,
                body,
                null);
        }

        public static PayloadLoadResult Failure(
            string message)
        {
            return new PayloadLoadResult(
                false,
                default,
                message);
        }
    }

    private sealed record Process(
        Guid ProcessId,
        long FlowVersionId,
        string FlowName,
        int FlowVersion,
        string BusinessKey,
        string State,
        string? CurrentStepKey);

    private sealed record ExistingSignal(
        Guid ProcessId,
        string SignalType,
        string BodyHash);

    private sealed record CurrentStep(
        string StepKey,
        string StepType,
        string? SignalType,
        string? StepOutcome);

    private sealed record StepInstance(
        Guid StepInstanceId);

    private sealed record TransitionRecord(
        string ToStepKey);

    private static readonly JsonSerializerOptions JsonOptions = new();
}