using System.Text.Json;

namespace Workflow.Tests.Helpers;

public sealed class WorkflowTestScenario(
    PostgreSqlFixture fixture)
{
    public const string WorkflowName = "workflow-smoke";

    public async Task<StartedWorkflow> StartAutomaticJobAsync(
        WorkflowTestCli cli,
        string? value = null)
    {
        var businessKey =
            $"workflow-test-{Guid.NewGuid():N}";

        var dataFile =
            Path.Combine(
                Path.GetTempPath(),
                $"workflow-test-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                dataFile,
                JsonSerializer.Serialize(
                    new
                    {
                        value =
                            value ?? "workflow-test"
                    }));

            var start =
                await cli.RunAsync(
                    "flow",
                    "start",
                    WorkflowName,
                    "--business-key",
                    businessKey,
                    "--data",
                    dataFile);

            var result =
                start.RootElement
                    .GetProperty("result");

            return new StartedWorkflow(
                result
                    .GetProperty("processId")
                    .GetGuid(),
                businessKey);
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
}

public sealed record StartedWorkflow(
    Guid ProcessId,
    string BusinessKey);