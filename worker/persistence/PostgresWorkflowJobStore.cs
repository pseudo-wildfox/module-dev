using ModuleDev.ActionRuntime;
using ModuleDev.Workflow;
using Npgsql;

namespace ModuleDev.Worker.Persistence;

public sealed class PostgresWorkflowJobStore : IWorkflowJobStore
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly TimeSpan _leaseDuration;

    public PostgresWorkflowJobStore(DbConnectionFactory connectionFactory, TimeSpan leaseDuration)
    {
        _connectionFactory = connectionFactory;
        _leaseDuration = leaseDuration;
    }

    public async Task<IReadOnlyList<WorkflowJob>> ClaimJobsAsync(
        string leaseOwner,
        int limit,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            select
                job_id,
                process_id,
                step_instance_id,
                execution_id,
                attempt_id,
                attempt_number,
                lease_version,
                flow_name,
                flow_version,
                step_key,
                service,
                module,
                action,
                action_version,
                request_schema::text,
                response_schema::text,
                outcomes::text,
                required_policy::text,
                timeout_ms,
                max_attempts,
                delays_ms::text,
                input_mapping::text,
                input_constants::text,
                process_data::text
            from workflow.claim_jobs(
                @owner,
                @limit,
                @lease_duration
            )
            """,
            connection
        );

        command.Parameters.AddWithValue("owner", leaseOwner);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.Add("lease_duration", NpgsqlTypes.NpgsqlDbType.Interval).Value =
            _leaseDuration;

        var jobs = new List<WorkflowJob>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(MapWorkflowJob(reader));
        }

        return jobs;
    }

    private static WorkflowJob MapWorkflowJob(NpgsqlDataReader reader)
    {
        return new WorkflowJob
        {
            JobId = reader.GetGuid(reader.GetOrdinal("job_id")),
            ProcessId = reader.GetGuid(reader.GetOrdinal("process_id")),
            StepInstanceId = reader.GetGuid(reader.GetOrdinal("step_instance_id")),
            ExecutionId = reader.GetGuid(reader.GetOrdinal("execution_id")),
            AttemptId = reader.GetGuid(reader.GetOrdinal("attempt_id")),
            AttemptNumber = reader.GetInt32(reader.GetOrdinal("attempt_number")),
            LeaseVersion = reader.GetInt64(reader.GetOrdinal("lease_version")),

            FlowName = reader.GetString(reader.GetOrdinal("flow_name")),
            FlowVersion = reader.GetInt32(reader.GetOrdinal("flow_version")),
            StepKey = reader.GetString(reader.GetOrdinal("step_key")),

            Service = reader.GetString(reader.GetOrdinal("service")),
            Module = reader.GetString(reader.GetOrdinal("module")),
            Action = reader.GetString(reader.GetOrdinal("action")),
            ActionVersion = reader.GetInt32(reader.GetOrdinal("action_version")),

            RequestSchema = reader.GetString(reader.GetOrdinal("request_schema")),
            ResponseSchema = reader.GetString(reader.GetOrdinal("response_schema")),
            Outcomes = reader.GetString(reader.GetOrdinal("outcomes")),
            RequiredPolicy = reader.GetString(reader.GetOrdinal("required_policy")),

            TimeoutMs = reader.GetInt32(reader.GetOrdinal("timeout_ms")),
            MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
            DelaysMs = reader.GetString(reader.GetOrdinal("delays_ms")),
            InputMapping = reader.GetString(reader.GetOrdinal("input_mapping")),
            InputConstants = reader.GetString(reader.GetOrdinal("input_constants")),
            ProcessData = reader.GetString(reader.GetOrdinal("process_data")),
        };
    }
}
