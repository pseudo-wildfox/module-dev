using System.Text.Json;
using Npgsql;
using Workflow.Tests.Helpers;

namespace Workflow.Tests;

[Collection("PostgreSQL")]
public sealed class WorkflowClaimPreparationTests
{
    private readonly PostgreSqlFixture _fixture;

    public WorkflowClaimPreparationTests(
        PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FlowStart_CreatesReadyAutomaticJob()
    {
        var cli =
            _fixture.CreateCli();

        var workflowMap =
            Path.Combine(
                _fixture.RepositoryRoot,
                "contracts",
                "course-1",
                "workflow-map.example.json");


        var businessKey =
            $"claim-test-{Guid.NewGuid():N}";

        var dataFile =
            Path.Combine(
                Path.GetTempPath(),
                $"workflow-test-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                dataFile,
                """
                {
                  "value": "hello"
                }
                """);

            var start =
                await cli.RunAsync(
                    "flow",
                    "start",
                    "workflow-smoke",
                    "--business-key",
                    businessKey,
                    "--data",
                    dataFile);

            var result =
                start.RootElement
                    .GetProperty("result");

            var processId =
                result
                    .GetProperty("processId")
                    .GetGuid();

            Assert.Equal(
                "process",
                result
                    .GetProperty("resource")
                    .GetString());

            Assert.Equal(
                "started",
                result
                    .GetProperty("operation")
                    .GetString());

            Assert.Equal(
                "workflow-smoke",
                result
                    .GetProperty("flowName")
                    .GetString());

            Assert.Equal(
                1,
                result
                    .GetProperty("flowVersion")
                    .GetInt32());

            await using var connection =
                new NpgsqlConnection(
                    _fixture.ConnectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT
                    p.process_id,
                    p.flow_name,
                    p.flow_version,
                    p.state,

                    s.step_instance_id,
                    s.step_key,
                    s.step_type,
                    s.state AS step_state,

                    j.job_id,
                    j.execution_id,
                    j.state AS job_state
                FROM workflow.process_instance AS p
                JOIN workflow.step_instance AS s
                  ON s.process_id = p.process_id
                LEFT JOIN workflow.workflow_job AS j
                  ON j.process_id = p.process_id
                 AND j.step_instance_id = s.step_instance_id
                WHERE p.process_id = @process_id;
                """;

            await using var command =
                new NpgsqlCommand(
                    sql,
                    connection);

            command.Parameters.AddWithValue(
                "process_id",
                processId);

            await using var reader =
                await command.ExecuteReaderAsync();

            Assert.True(
                await reader.ReadAsync());

            Assert.Equal(
                processId,
                reader.GetGuid(0));

            Assert.Equal(
                "workflow-smoke",
                reader.GetString(1));

            Assert.Equal(
                1,
                reader.GetInt32(2));

            Assert.Equal(
                "RUNNING",
                reader.GetString(3));

            Assert.Equal(
                "invoke_canary",
                reader.GetString(5));

            Assert.Equal(
                "AUTOMATIC",
                reader.GetString(6));

            Assert.Equal(
                "READY",
                reader.GetString(7));

            Assert.False(
                reader.IsDBNull(8));

            Assert.False(
                reader.IsDBNull(9));

            Assert.Equal(
                "READY",
                reader.GetString(10));
        }
        finally
        {
            try
            {
                File.Delete(dataFile);
            }
            catch
            {
                // Test cleanup should not hide the actual test result.
            }
        }
    }
}