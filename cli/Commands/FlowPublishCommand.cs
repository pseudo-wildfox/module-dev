using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using ModuleDev.Workflow;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.Cli.Commands;

public sealed class FlowPublishCommand
{
    private readonly CliDatabase _database;
    private readonly WorkflowMapValidator _validator;
    private readonly CliOutput _output;

    public FlowPublishCommand(
        CliDatabase database,
        WorkflowMapValidator validator,
        CliOutput output)
    {
        _database = database;
        _validator = validator;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string mapPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mapPath))
        {
            return _output.Error(
                code: "request.invalid",
                message: "workflow map file not found");
        }

        string mapJson;

        try
        {
            mapJson = await File.ReadAllTextAsync(
                mapPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "request.invalid",
                message: "failed to read workflow map file");
        }

        WorkflowMapValidationResult validation;

        try
        {
            validation = await _validator.ValidateAsync(
                mapJson,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "manifest.invalid",
                message: "workflow map validation failed");
        }

        if (!validation.IsValid)
        {
            return _output.Error(
                code: "manifest.invalid",
                message: "workflow map is invalid",
                details: new
                {
                    errors = validation.Errors
                });
        }

        WorkflowMap map;

        try
        {
            map = WorkflowMapLoader.Load(mapJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "manifest.invalid",
                message: "workflow map could not be loaded");
        }

        var mapHash = CalculateMapHash(map);

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        var flowDefinitionId =
            await GetOrCreateFlowDefinitionAsync(
                connection,
                transaction,
                map.FlowName,
                cancellationToken);

        var existing =
            await GetExistingVersionAsync(
                connection,
                transaction,
                flowDefinitionId,
                map.Version,
                cancellationToken);

        if (existing is not null)
        {
            if (string.Equals(
                    existing.ManifestHash,
                    mapHash,
                    StringComparison.Ordinal))
            {
                await transaction.CommitAsync(
                    cancellationToken);

                return _output.Success(new
                {
                    resource = "flow",
                    operation = "published",
                    flowName = map.FlowName,
                    flowVersion = map.Version
                });
            }

            await transaction.RollbackAsync(
                cancellationToken);

            return _output.Error(
                code: "manifest.conflict",
                message: "published flow version is immutable");
        }

        var flowVersionId =
            await InsertFlowVersionAsync(
                connection,
                transaction,
                flowDefinitionId,
                map,
                mapJson,
                mapHash,
                cancellationToken);

        var stepIds =
            await InsertStepDefinitionsAsync(
                connection,
                transaction,
                flowVersionId,
                map,
                cancellationToken);

        await InsertTaskDefinitionsAsync(
            connection,
            transaction,
            map,
            stepIds,
            cancellationToken);

        await InsertTransitionDefinitionsAsync(
            connection,
            transaction,
            flowVersionId,
            map,
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return _output.Success(new
        {
            resource = "flow",
            operation = "published",
            flowName = map.FlowName,
            flowVersion = map.Version
        });
    }

    private static async Task<long> GetOrCreateFlowDefinitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string flowName,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO workflow.flow_definition
            (
                flow_name
            )
            VALUES
            (
                @flow_name
            )
            ON CONFLICT (flow_name)
            DO NOTHING
            RETURNING id;
            """;

        await using (var insertCommand =
            new NpgsqlCommand(
                insertSql,
                connection,
                transaction))
        {
            insertCommand.Parameters.AddWithValue(
                "flow_name",
                flowName);

            var insertedId =
                await insertCommand.ExecuteScalarAsync(
                    cancellationToken);

            if (insertedId is not null)
            {
                return Convert.ToInt64(insertedId);
            }
        }

        const string selectSql = """
            SELECT id
            FROM workflow.flow_definition
            WHERE flow_name = @flow_name
            FOR UPDATE;
            """;

        await using var selectCommand =
            new NpgsqlCommand(
                selectSql,
                connection,
                transaction);

        selectCommand.Parameters.AddWithValue(
            "flow_name",
            flowName);

        var existingId =
            await selectCommand.ExecuteScalarAsync(
                cancellationToken);

        if (existingId is null)
        {
            throw new InvalidOperationException(
                "flow definition could not be resolved");
        }

        return Convert.ToInt64(existingId);
    }

    private static async Task<ExistingFlowVersion?> GetExistingVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowDefinitionId,
        int version,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT manifest_hash
            FROM workflow.flow_version
            WHERE flow_definition_id = @flow_definition_id
              AND version = @version
            FOR UPDATE;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "flow_definition_id",
            flowDefinitionId);

        command.Parameters.AddWithValue(
            "version",
            version);

        var hash =
            await command.ExecuteScalarAsync(
                cancellationToken) as string;

        return hash is null
            ? null
            : new ExistingFlowVersion(hash);
    }

    private static async Task<long> InsertFlowVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowDefinitionId,
        WorkflowMap map,
        string mapJson,
        string mapHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.flow_version
            (
                flow_definition_id,
                version,
                contract_version,
                start_step,
                status,
                map_json,
                manifest_hash,
                is_active
            )
            VALUES
            (
                @flow_definition_id,
                @version,
                @contract_version,
                @start_step,
                'PUBLISHED',
                @map_json,
                @manifest_hash,
                false
            )
            RETURNING id;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "flow_definition_id",
            flowDefinitionId);

        command.Parameters.AddWithValue(
            "version",
            map.Version);

        command.Parameters.AddWithValue(
            "contract_version",
            map.ContractVersion);

        command.Parameters.AddWithValue(
            "start_step",
            map.StartStep);

        var mapParameter =
            command.Parameters.Add(
                "map_json",
                NpgsqlDbType.Jsonb);

        mapParameter.Value = mapJson;

        command.Parameters.AddWithValue(
            "manifest_hash",
            mapHash);

        var id =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (id is null)
        {
            throw new InvalidOperationException(
                "flow version could not be inserted");
        }

        return Convert.ToInt64(id);
    }

    private static async Task<Dictionary<string, long>> InsertStepDefinitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        WorkflowMap map,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.step_definition
            (
                flow_version_id,
                step_key,
                step_type,
                signal_type,
                step_outcome,
                allowed_outcomes
            )
            VALUES
            (
                @flow_version_id,
                @step_key,
                @step_type,
                @signal_type,
                @step_outcome,
                @allowed_outcomes
            )
            RETURNING id;
            """;

        var result = new Dictionary<string, long>(
            StringComparer.Ordinal);

        foreach (var step in map.Steps)
        {
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
                step.Key);

            var stepType = GetStepType(step);

            command.Parameters.AddWithValue(
                "step_type",
                stepType);

            object? signalType = step switch
            {
                WaitSignalStep wait => wait.SignalType,
                _ => null
            };

            command.Parameters.AddWithValue(
                "signal_type",
                signalType ?? DBNull.Value);

            object? stepOutcome = step switch
            {
                WaitSignalStep wait => wait.Outcome,
                EndStep end => end.Outcome,
                _ => null
            };

            command.Parameters.AddWithValue(
                "step_outcome",
                stepOutcome ?? DBNull.Value);

            var allowedParameter =
                command.Parameters.Add(
                    "allowed_outcomes",
                    NpgsqlDbType.Jsonb);

            if (step is ManualStep manual)
            {
                allowedParameter.Value =
                    JsonSerializer.Serialize(
                        manual.AllowedOutcomes,
                        JsonOptions);
            }
            else
            {
                allowedParameter.Value = DBNull.Value;
            }

            var id =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            if (id is null)
            {
                throw new InvalidOperationException(
                    $"step '{step.Key}' could not be inserted");
            }

            result.Add(
                step.Key,
                Convert.ToInt64(id));
        }

        return result;
    }

    private static async Task InsertTaskDefinitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WorkflowMap map,
        IReadOnlyDictionary<string, long> stepIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.task_definition
            (
                step_definition_id,
                service,
                module,
                action,
                action_version,
                required_policy,
                timeout_ms,
                retry_max_attempts,
                retry_delays_ms,
                input_mapping,
                input_constants
            )
            VALUES
            (
                @step_definition_id,
                @service,
                @module,
                @action,
                @action_version,
                @required_policy,
                @timeout_ms,
                @retry_max_attempts,
                @retry_delays_ms,
                @input_mapping,
                @input_constants
            );
            """;

        foreach (var step in map.Steps)
        {
            if (step is not AutomaticStep automatic)
            {
                continue;
            }

            await using var command =
                new NpgsqlCommand(
                    sql,
                    connection,
                    transaction);

            command.Parameters.AddWithValue(
                "step_definition_id",
                stepIds[step.Key]);

            command.Parameters.AddWithValue(
                "service",
                automatic.Task.Service);

            command.Parameters.AddWithValue(
                "module",
                automatic.Task.Module);

            command.Parameters.AddWithValue(
                "action",
                automatic.Task.Action);

            command.Parameters.AddWithValue(
                "action_version",
                automatic.Task.ActionVersion);

            AddJsonbParameter(
                command,
                "required_policy",
                automatic.Task.RequiredPolicy);

            command.Parameters.AddWithValue(
                "timeout_ms",
                automatic.Task.TimeoutMs);

            command.Parameters.AddWithValue(
                "retry_max_attempts",
                automatic.Task.Retry.MaxAttempts);

            AddJsonbParameter(
                command,
                "retry_delays_ms",
                automatic.Task.Retry.DelaysMs);

            AddJsonbParameter(
                command,
                "input_mapping",
                automatic.Task.InputMapping);

            AddJsonbParameter(
                command,
                "input_constants",
                automatic.Task.InputConstants);
            
            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static async Task InsertTransitionDefinitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long flowVersionId,
        WorkflowMap map,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO workflow.transition_definition
            (
                flow_version_id,
                from_step_key,
                outcome,
                to_step_key
            )
            VALUES
            (
                @flow_version_id,
                @from_step_key,
                @outcome,
                @to_step_key
            );
            """;

        foreach (var transition in map.Transitions)
        {
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
                transition.From);

            command.Parameters.AddWithValue(
                "outcome",
                transition.Outcome);

            command.Parameters.AddWithValue(
                "to_step_key",
                transition.To);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static string GetStepType(FlowStep step)
    {
        return step switch
        {
            AutomaticStep => "AUTOMATIC",
            WaitSignalStep => "WAIT_SIGNAL",
            ManualStep => "MANUAL",
            EndStep => "END",
            _ => throw new InvalidOperationException(
                $"unsupported workflow step type: {step.GetType().Name}")
        };
    }

    private static string CalculateMapHash(
        WorkflowMap map)
    {
        var canonicalMap = new
        {
            contract_version = map.ContractVersion,
            flow_name = map.FlowName,
            version = map.Version,
            start_step = map.StartStep,
            steps = map.Steps,
            transitions = map.Transitions
        };

        var json = JsonSerializer.Serialize(
            canonicalMap,
            JsonOptions);

        var bytes = Encoding.UTF8.GetBytes(json);

        return Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    private static void AddJsonbParameter(
        NpgsqlCommand command,
        string name,
        object value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                NpgsqlDbType.Jsonb);

        parameter.Value =
            JsonSerializer.Serialize(
                value,
                JsonOptions);
    }

    private sealed record ExistingFlowVersion(
        string ManifestHash);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}