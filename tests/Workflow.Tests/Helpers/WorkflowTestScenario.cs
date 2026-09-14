using System.Text.Json;
using Npgsql;

namespace Workflow.Tests.Helpers;

public sealed class WorkflowTestScenario(PostgreSqlFixture fixture)
{
    public const string WorkflowName = "workflow-smoke";

    public async Task<StartedWorkflow> StartAutomaticJobAsync(
        WorkflowTestCli cli,
        string? value = null
    )
    {
        var businessKey = $"workflow-test-{Guid.NewGuid():N}";

        var dataFile = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                dataFile,
                JsonSerializer.Serialize(new { value = value ?? "workflow-test" })
            );

            var start = await cli.RunAsync(
                "flow",
                "start",
                WorkflowName,
                "--business-key",
                businessKey,
                "--data",
                dataFile
            );

            var result = start.RootElement.GetProperty("result");

            return new StartedWorkflow(result.GetProperty("processId").GetGuid(), businessKey);
        }
        finally
        {
            try
            {
                File.Delete(dataFile);
            }
            catch
            {
                // Do not hide the actual test result.
            }
        }
    }

    public static async Task<ClaimedJob> ClaimJobAsync(
        NpgsqlConnection connection,
        string owner,
        TimeSpan leaseDuration
    )
    {
        const string sql = """
            SELECT
                job_id,
                process_id,
                step_instance_id,
                execution_id,
                attempt_id,
                lease_version
            FROM workflow.claim_jobs(
                @owner,
                @limit,
                @lease_duration
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("owner", owner);

        command.Parameters.AddWithValue("limit", 1);

        command.Parameters.AddWithValue("lease_duration", leaseDuration);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        var job = new ClaimedJob(
            JobId: reader.GetGuid(0),
            ProcessId: reader.GetGuid(1),
            StepInstanceId: reader.GetGuid(2),
            ExecutionId: reader.GetGuid(3),
            AttemptId: reader.GetGuid(4),
            LeaseVersion: reader.GetInt64(5)
        );

        Assert.False(await reader.ReadAsync());

        return job;
    }
}

public sealed record StartedWorkflow(Guid ProcessId, string BusinessKey);

public sealed record ClaimedJob(
    Guid JobId,
    Guid ProcessId,
    Guid StepInstanceId,
    Guid ExecutionId,
    Guid AttemptId,
    long LeaseVersion
);
