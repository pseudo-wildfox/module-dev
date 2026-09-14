using Npgsql;
using NpgsqlTypes;
using Workflow.Tests.Helpers;

namespace Workflow.Tests;

[Collection("PostgreSQL")]
public sealed class WorkflowClaimJobsTests
{
    private readonly PostgreSqlFixture _fixture;

    public WorkflowClaimJobsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClaimJobs_ClaimsReadyJobAndCreatesAttempt()
    {
        await _fixture.ResetWorkflowStateAsync();

        var cli = _fixture.CreateCli();

        var scenario = new WorkflowTestScenario(_fixture);

        var started = await scenario.StartAutomaticJobAsync(cli);

        var processId = started.ProcessId;

        Guid jobId;
        Guid executionId;

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    job_id,
                    execution_id,
                    state,
                    lease_owner,
                    lease_version,
                    attempt_count
                FROM workflow.workflow_job
                WHERE process_id = @process_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("process_id", processId);

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            jobId = reader.GetGuid(0);

            executionId = reader.GetGuid(1);

            Assert.Equal("READY", reader.GetString(2));

            Assert.True(reader.IsDBNull(3));

            Assert.Equal(0L, reader.GetInt64(4));

            Assert.Equal(0, reader.GetInt32(5));
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
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
                    timeout_ms,
                    max_attempts
                FROM workflow.claim_jobs(
                    @owner,
                    @limit,
                    @lease_duration
                );
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("owner", "worker-a");

            command.Parameters.AddWithValue("limit", 1);

            command.Parameters.AddWithValue("lease_duration", TimeSpan.FromSeconds(2));

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            Assert.Equal(jobId, reader.GetGuid(0));

            Assert.Equal(processId, reader.GetGuid(1));

            Assert.Equal(executionId, reader.GetGuid(3));

            var attemptId = reader.GetGuid(4);

            Assert.NotEqual(Guid.Empty, attemptId);

            Assert.Equal(1, reader.GetInt32(5));

            Assert.Equal(1L, reader.GetInt64(6));

            Assert.Equal("workflow-smoke", reader.GetString(7));

            Assert.Equal(1, reader.GetInt32(8));

            Assert.Equal("invoke_canary", reader.GetString(9));

            Assert.Equal("postgres", reader.GetString(10));

            Assert.Equal("training", reader.GetString(11));

            Assert.Equal("canary", reader.GetString(12));

            Assert.Equal(1, reader.GetInt32(13));

            Assert.Equal(2000, reader.GetInt32(14));

            Assert.Equal(3, reader.GetInt32(15));

            Assert.False(await reader.ReadAsync());
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    j.state,
                    j.lease_owner,
                    j.lease_version,
                    j.attempt_count,
                    j.execution_id,

                    a.attempt_id,
                    a.attempt_number,
                    a.status,
                    a.lease_version
                FROM workflow.workflow_job AS j
                LEFT JOIN workflow.task_attempt AS a
                  ON a.job_id = j.job_id
                WHERE j.job_id = @job_id
                ORDER BY a.attempt_number;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("job_id", jobId);

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            Assert.Equal("LEASED", reader.GetString(0));

            Assert.Equal("worker-a", reader.GetString(1));

            Assert.Equal(1L, reader.GetInt64(2));

            Assert.Equal(1, reader.GetInt32(3));

            Assert.Equal(executionId, reader.GetGuid(4));

            Assert.NotEqual(Guid.Empty, reader.GetGuid(5));

            Assert.Equal(1, reader.GetInt32(6));

            Assert.Equal("RUNNING", reader.GetString(7));

            Assert.Equal(1L, reader.GetInt64(8));

            Assert.False(await reader.ReadAsync());
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT COUNT(*)
                FROM workflow.claim_jobs(
                    @owner,
                    @limit,
                    @lease_duration
                );
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("owner", "worker-b");

            command.Parameters.AddWithValue("limit", 1);

            command.Parameters.AddWithValue("lease_duration", TimeSpan.FromSeconds(2));

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());

            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task ClaimJobs_ClaimsDueRetryWaitJob()
    {
        await _fixture.ResetWorkflowStateAsync();

        var cli = _fixture.CreateCli();

        var scenario = new WorkflowTestScenario(_fixture);

        var started = await scenario.StartAutomaticJobAsync(cli, "retry-test");

        var processId = started.ProcessId;

        Guid jobId;
        Guid executionId;

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    job_id,
                    execution_id
                FROM workflow.workflow_job
                WHERE process_id = @process_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("process_id", processId);

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            jobId = reader.GetGuid(0);

            executionId = reader.GetGuid(1);

            Assert.False(await reader.ReadAsync());
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            //TODO: change this to use workflow.fail_job() instead of directly updating the job state in the database
            const string sql = """
                UPDATE workflow.workflow_job
                SET
                    state = 'RETRY_WAIT',
                    next_attempt_at = clock_timestamp() - interval '1 second',
                    updated_at = clock_timestamp()
                WHERE job_id = @job_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("job_id", jobId);

            var affected = await command.ExecuteNonQueryAsync();

            Assert.Equal(1, affected);
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    j.state,
                    j.lease_owner,
                    j.lease_version,
                    j.attempt_count,
                    j.next_attempt_at
                FROM workflow.workflow_job as j
                WHERE job_id = @job_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("job_id", jobId);

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            Assert.Equal("RETRY_WAIT", reader.GetString(0));

            Assert.True(reader.IsDBNull(1));

            Assert.Equal(0L, reader.GetInt64(2));

            Assert.Equal(0, reader.GetInt32(3));

            Assert.False(reader.IsDBNull(4));
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    job_id,
                    execution_id,
                    attempt_id,
                    attempt_number,
                    lease_version
                FROM workflow.claim_jobs(
                    @owner,
                    @limit,
                    @lease_duration
                );
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("owner", "worker-a");

            command.Parameters.AddWithValue("limit", 1);

            command.Parameters.AddWithValue("lease_duration", TimeSpan.FromSeconds(2));

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            Assert.Equal(jobId, reader.GetGuid(0));

            Assert.Equal(executionId, reader.GetGuid(1));

            Assert.NotEqual(Guid.Empty, reader.GetGuid(2));

            Assert.Equal(1, reader.GetInt32(3));

            Assert.Equal(1L, reader.GetInt64(4));

            Assert.False(await reader.ReadAsync());
        }

        await using var finalConnection = new NpgsqlConnection(_fixture.ConnectionString);

        await finalConnection.OpenAsync();

        const string finalSql = """
            SELECT
                j.state,
                j.lease_owner,
                j.lease_version,
                j.attempt_count,
                j.next_attempt_at,

                a.execution_id,
                a.attempt_number,
                a.status,
                a.lease_version
            FROM workflow.workflow_job AS j
            LEFT JOIN workflow.task_attempt AS a
              ON a.job_id = j.job_id
            WHERE j.job_id = @job_id
            ORDER BY a.attempt_number;
            """;

        await using var finalCommand = new NpgsqlCommand(finalSql, finalConnection);

        finalCommand.Parameters.AddWithValue("job_id", jobId);

        await using var finalReader = await finalCommand.ExecuteReaderAsync();

        Assert.True(await finalReader.ReadAsync());

        Assert.Equal("LEASED", finalReader.GetString(0));

        Assert.Equal("worker-a", finalReader.GetString(1));

        Assert.Equal(1L, finalReader.GetInt64(2));

        Assert.Equal(1, finalReader.GetInt32(3));

        Assert.True(finalReader.IsDBNull(4));

        Assert.Equal(executionId, finalReader.GetGuid(5));

        Assert.Equal(1, finalReader.GetInt32(6));

        Assert.Equal("RUNNING", finalReader.GetString(7));

        Assert.Equal(1L, finalReader.GetInt64(8));

        Assert.False(await finalReader.ReadAsync());
    }

    [Fact]
    public async Task ClaimJobs_DoesNotClaimRetryWaitJobBeforeNextAttemptTime()
    {
        await _fixture.ResetWorkflowStateAsync();

        var cli = _fixture.CreateCli();

        var scenario = new WorkflowTestScenario(_fixture);

        var started = await scenario.StartAutomaticJobAsync(cli, "retry-future-test");

        var processId = started.ProcessId;

        Guid jobId;

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    job_id
                FROM workflow.workflow_job
                WHERE process_id = @process_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("process_id", processId);

            jobId = (Guid)(await command.ExecuteScalarAsync())!;
        }

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                UPDATE workflow.workflow_job
                SET
                    state = 'RETRY_WAIT',
                    next_attempt_at =
                        clock_timestamp()
                        + interval '10 minutes',
                    updated_at = clock_timestamp()
                WHERE job_id = @job_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("job_id", jobId);

            var affected = await command.ExecuteNonQueryAsync();

            Assert.Equal(1, affected);
        }

        await using var claimConnection = new NpgsqlConnection(_fixture.ConnectionString);

        await claimConnection.OpenAsync();

        const string claimSql = """
            SELECT COUNT(*)
            FROM workflow.claim_jobs(
                @owner,
                @limit,
                @lease_duration
            );
            """;

        await using var claimCommand = new NpgsqlCommand(claimSql, claimConnection);

        claimCommand.Parameters.AddWithValue("owner", "worker-a");

        claimCommand.Parameters.AddWithValue("limit", 1);

        claimCommand.Parameters.AddWithValue("lease_duration", TimeSpan.FromSeconds(2));

        var count = Convert.ToInt32(await claimCommand.ExecuteScalarAsync());

        Assert.Equal(0, count);

        await using var verifyConnection = new NpgsqlConnection(_fixture.ConnectionString);

        await verifyConnection.OpenAsync();

        const string verifySql = """
            SELECT
                j.state,
                j.lease_owner,
                j.lease_version,
                j.attempt_count
            FROM workflow.workflow_job as j
            WHERE job_id = @job_id;
            """;

        await using var verifyCommand = new NpgsqlCommand(verifySql, verifyConnection);

        verifyCommand.Parameters.AddWithValue("job_id", jobId);

        await using var reader = await verifyCommand.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        Assert.Equal("RETRY_WAIT", reader.GetString(0));

        Assert.True(reader.IsDBNull(1));

        Assert.Equal(0L, reader.GetInt64(2));

        Assert.Equal(0, reader.GetInt32(3));

        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task ClaimJobs_RespectsRequestedLimit()
    {
        await _fixture.ResetWorkflowStateAsync();

        var cli = _fixture.CreateCli();

        var scenario = new WorkflowTestScenario(_fixture);

        var first = await scenario.StartAutomaticJobAsync(cli, "limit-test-1");

        var second = await scenario.StartAutomaticJobAsync(cli, "limit-test-2");

        var third = await scenario.StartAutomaticJobAsync(cli, "limit-test-3");

        var processIds = new[] { first.ProcessId, second.ProcessId, third.ProcessId };

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);

        await connection.OpenAsync();

        const string claimSql = """
            SELECT
                job_id
            FROM workflow.claim_jobs(
                @owner,
                @limit,
                @lease_duration
            )
            ORDER BY job_id;
            """;

        await using var claimCommand = new NpgsqlCommand(claimSql, connection);

        claimCommand.Parameters.AddWithValue("owner", "worker-a");

        claimCommand.Parameters.AddWithValue("limit", 2);

        claimCommand.Parameters.AddWithValue("lease_duration", TimeSpan.FromSeconds(2));

        var claimedJobIds = new List<Guid>();

        await using (var reader = await claimCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                claimedJobIds.Add(reader.GetGuid(0));
            }
        }

        Assert.Equal(2, claimedJobIds.Count);

        Assert.Equal(2, claimedJobIds.Distinct().Count());

        const string stateSql = """
            SELECT
                state,
                lease_owner,
                lease_version,
                attempt_count
            FROM workflow.workflow_job
            WHERE process_id = ANY(@process_ids)
            ORDER BY process_id;
            """;

        await using var stateCommand = new NpgsqlCommand(stateSql, connection);

        stateCommand.Parameters.AddWithValue("process_ids", processIds);

        var leasedCount = 0;
        var readyCount = 0;

        await using (var reader = await stateCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var state = reader.GetString(0);

                switch (state)
                {
                    case "LEASED":

                        leasedCount++;

                        Assert.Equal("worker-a", reader.GetString(1));

                        Assert.Equal(1L, reader.GetInt64(2));

                        Assert.Equal(1, reader.GetInt32(3));

                        break;

                    case "READY":

                        readyCount++;

                        Assert.True(reader.IsDBNull(1));

                        Assert.Equal(0L, reader.GetInt64(2));

                        Assert.Equal(0, reader.GetInt32(3));

                        break;

                    default:

                        Assert.Fail($"Unexpected job state: {state}");

                        break;
                }
            }
        }

        Assert.Equal(2, leasedCount);

        Assert.Equal(1, readyCount);
    }

    [Fact]
    public async Task ClaimJobs_TwoWorkersClaimDifferentJobs()
    {
        await _fixture.ResetWorkflowStateAsync();

        var cli = _fixture.CreateCli();

        var scenario = new WorkflowTestScenario(_fixture);

        var first = await scenario.StartAutomaticJobAsync(cli, "two-worker-test-1");

        var second = await scenario.StartAutomaticJobAsync(cli, "two-worker-test-2");

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);

        await connection.OpenAsync();

        const string claimSql = """
            SELECT
                job_id,
                process_id,
                execution_id,
                lease_version,
                attempt_number
            FROM workflow.claim_jobs(
                @owner,
                @limit,
                @lease_duration
            );
            """;

        var firstJob = await ClaimSingleJobAsync(connection, claimSql, "worker-a");

        var secondJob = await ClaimSingleJobAsync(connection, claimSql, "worker-b");

        Assert.NotEqual(firstJob.JobId, secondJob.JobId);

        Assert.NotEqual(firstJob.ProcessId, secondJob.ProcessId);

        Assert.NotEqual(firstJob.ExecutionId, secondJob.ExecutionId);

        Assert.Equal(first.ProcessId, firstJob.ProcessId);

        Assert.Equal(second.ProcessId, secondJob.ProcessId);

        Assert.Equal(1L, firstJob.LeaseVersion);

        Assert.Equal(1L, secondJob.LeaseVersion);

        Assert.Equal(1, firstJob.AttemptNumber);

        Assert.Equal(1, secondJob.AttemptNumber);

        var firstOwner = await GetLeaseOwnerAsync(connection, firstJob.JobId);

        var secondOwner = await GetLeaseOwnerAsync(connection, secondJob.JobId);

        Assert.Equal("worker-a", firstOwner);

        Assert.Equal("worker-b", secondOwner);

        const string leasedCountSql = """
            SELECT COUNT(*)
            FROM workflow.workflow_job
            WHERE state = 'LEASED';
            """;

        await using var leasedCountCommand = new NpgsqlCommand(leasedCountSql, connection);

        var leasedCount = Convert.ToInt32(await leasedCountCommand.ExecuteScalarAsync());

        Assert.Equal(2, leasedCount);
    }

    private static async Task<ClaimedJobInfo> ClaimSingleJobAsync(
        NpgsqlConnection connection,
        string sql,
        string owner
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("owner", owner);

        command.Parameters.AddWithValue("limit", 1);

        command.Parameters.AddWithValue("lease_duration", TimeSpan.FromSeconds(2));

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        var result = new ClaimedJobInfo(
            JobId: reader.GetGuid(0),
            ProcessId: reader.GetGuid(1),
            ExecutionId: reader.GetGuid(2),
            LeaseVersion: reader.GetInt64(3),
            AttemptNumber: reader.GetInt32(4)
        );

        Assert.False(await reader.ReadAsync());

        return result;
    }

    private static async Task<string> GetLeaseOwnerAsync(NpgsqlConnection connection, Guid jobId)
    {
        const string sql = """
            SELECT lease_owner
            FROM workflow.workflow_job
            WHERE job_id = @job_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("job_id", jobId);

        var value = await command.ExecuteScalarAsync();

        Assert.NotNull(value);

        return (string)value;
    }

    private sealed record ClaimedJobInfo(
        Guid JobId,
        Guid ProcessId,
        Guid ExecutionId,
        long LeaseVersion,
        int AttemptNumber
    );

    [Fact]
    public async Task FinishJob_CompletesAutomaticStepAndMovesToWaitSignal()
    {
        await _fixture.ResetWorkflowStateAsync();

        var cli = _fixture.CreateCli();

        var scenario = new WorkflowTestScenario(_fixture);

        var started = await scenario.StartAutomaticJobAsync(cli, "finish-test");

        var processId = started.ProcessId;

        Guid jobId;
        Guid executionId;

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    job_id,
                    execution_id
                FROM workflow.workflow_job
                WHERE process_id = @process_id;
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("process_id", processId);

            await using var jobReader = await command.ExecuteReaderAsync();

            Assert.True(await jobReader.ReadAsync());

            jobId = jobReader.GetGuid(0);

            executionId = jobReader.GetGuid(1);

            Assert.False(await jobReader.ReadAsync());
        }

        ClaimedJob claimed;

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            claimed = await WorkflowTestScenario.ClaimJobAsync(
                connection,
                "worker-a",
                TimeSpan.FromSeconds(2)
            );
        }

        Assert.Equal(jobId, claimed.JobId);

        Assert.Equal(processId, claimed.ProcessId);

        Assert.Equal(executionId, claimed.ExecutionId);

        Assert.Equal(1L, claimed.LeaseVersion);

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();

            const string sql = """
                SELECT workflow.finish_job(
                    @job_id,
                    @owner,
                    @lease_version,
                    @outcome,
                    @result
                );
                """;

            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("job_id", claimed.JobId);

            command.Parameters.AddWithValue("owner", "worker-a");

            command.Parameters.AddWithValue("lease_version", claimed.LeaseVersion);

            command.Parameters.AddWithValue("outcome", "APPLIED");

            var resultParameter = command.Parameters.Add("result", NpgsqlDbType.Jsonb);

            resultParameter.Value = """
                {
                  "stored": true
                }
                """;

            await command.ExecuteScalarAsync();
        }

        await using var verifyConnection = new NpgsqlConnection(_fixture.ConnectionString);

        await verifyConnection.OpenAsync();

        const string verifySql = """
            SELECT
                j.state,
                j.lease_owner,
                j.lease_version,
                j.attempt_count,

                a.status,
                a.outcome,
                a.lease_version AS attempt_lease_version,

                s.state AS step_state,
                s.outcome AS step_outcome,

                p.state AS process_state,
                p.current_step_key

            FROM workflow.workflow_job AS j

            JOIN workflow.task_attempt AS a
              ON a.job_id = j.job_id

            JOIN workflow.step_instance AS s
              ON s.step_instance_id = j.step_instance_id
             AND s.process_id = j.process_id

            JOIN workflow.process_instance AS p
              ON p.process_id = j.process_id

            WHERE j.job_id = @job_id
            ORDER BY a.attempt_number;
            """;

        await using var verifyCommand = new NpgsqlCommand(verifySql, verifyConnection);

        verifyCommand.Parameters.AddWithValue("job_id", claimed.JobId);

        await using var reader = await verifyCommand.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        // Job was successfully finished.
        Assert.Equal("SUCCEEDED", reader.GetString(0));

        Assert.True(reader.IsDBNull(1));

        Assert.Equal(claimed.LeaseVersion, reader.GetInt64(2));

        Assert.Equal(1, reader.GetInt32(3));

        // Attempt was successfully finished.
        Assert.Equal("SUCCEEDED", reader.GetString(4));

        Assert.Equal("APPLIED", reader.GetString(5));

        Assert.Equal(claimed.LeaseVersion, reader.GetInt64(6));

        // Current automatic step was completed.
        Assert.Equal("COMPLETED", reader.GetString(7));

        Assert.Equal("APPLIED", reader.GetString(8));

        // Transition APPLIED moved the process to wait_result.
        Assert.Equal("WAITING_SIGNAL", reader.GetString(9));

        Assert.Equal("wait_result", reader.GetString(10));

        Assert.False(await reader.ReadAsync());
    }
}
