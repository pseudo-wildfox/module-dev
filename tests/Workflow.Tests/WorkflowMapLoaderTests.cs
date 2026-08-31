using System.Text.Json;
using ModuleDev.Workflow;

namespace ModuleDev.Workflow.Tests;

public sealed class WorkflowMapLoaderTests
{
    [Fact]
    public void Load_ExampleMap_CreatesExpectedStepTypes()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "contracts",
            "course-1",
            "workflow-map.example.json");

        var json = File.ReadAllText(path);

        var map = WorkflowMapLoader.Load(json);

        Assert.Equal("course-1", map.ContractVersion);
        Assert.Equal("workflow-smoke", map.FlowName);
        Assert.Equal(1, map.Version);
        Assert.Equal("invoke_canary", map.StartStep);

        Assert.Equal(3, map.Steps.Count);

        Assert.IsType<AutomaticStep>(map.Steps[0]);
        Assert.IsType<WaitSignalStep>(map.Steps[1]);
        Assert.IsType<EndStep>(map.Steps[2]);
    }

    [Fact]
    public void Load_ExampleMap_PopulatesAutomaticTask()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "contracts",
            "course-1",
            "workflow-map.example.json");

        var map = WorkflowMapLoader.Load(File.ReadAllText(path));

        var step = Assert.IsType<AutomaticStep>(map.Steps[0]);

        Assert.Equal("invoke_canary", step.Key);
        Assert.Equal("postgres", step.Task.Service);
        Assert.Equal("training", step.Task.Module);
        Assert.Equal("canary", step.Task.Action);
        Assert.Equal(1, step.Task.ActionVersion);
        Assert.Equal(
            new[] { "workflow:execute" },
            step.Task.RequiredPolicy);
        Assert.Equal(2000, step.Task.TimeoutMs);
        Assert.Equal(3, step.Task.Retry.MaxAttempts);
        Assert.Equal(new[] { 200, 400 }, step.Task.Retry.DelaysMs);
    }

    [Fact]
    public void Load_ExampleMap_PopulatesWaitAndEndSteps()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "contracts",
            "course-1",
            "workflow-map.example.json");

        var map = WorkflowMapLoader.Load(File.ReadAllText(path));

        var wait = Assert.IsType<WaitSignalStep>(map.Steps[1]);
        Assert.Equal("wait_result", wait.Key);
        Assert.Equal("training.completed", wait.SignalType);
        Assert.Equal("RECEIVED", wait.Outcome);

        var end = Assert.IsType<EndStep>(map.Steps[2]);
        Assert.Equal("done", end.Key);
        Assert.Equal("COMPLETED", end.Outcome);
    }

    [Fact]
    public void Load_ManualStep_CreatesManualStep()
    {
        const string json = """
        {
        "contract_version": "course-1",
        "flow_name": "manual-test",
        "version": 1,
        "start_step": "review",
        "steps": [
            {
            "key": "review",
            "type": "manual",
            "allowed_outcomes": ["APPROVED", "REJECTED"]
            },
            {
            "key": "done",
            "type": "end",
            "outcome": "COMPLETED"
            }
        ],
        "transitions": [
            {
            "from": "review",
            "outcome": "APPROVED",
            "to": "done"
            }
        ]
        }
        """;

        var map = WorkflowMapLoader.Load(json);

        var manual = Assert.IsType<ManualStep>(map.Steps[0]);

        Assert.Equal("review", manual.Key);
        Assert.Equal(
            new[] { "APPROVED", "REJECTED" },
            manual.AllowedOutcomes);
    }

    [Fact]
    public void Load_AutomaticStep_PreservesInputConstants()
    {
        const string json = """
        {
        "contract_version": "course-1",
        "flow_name": "constants-test",
        "version": 1,
        "start_step": "invoke",
        "steps": [
            {
            "key": "invoke",
            "type": "automatic",
            "task": {
                "service": "postgres",
                "module": "training",
                "action": "canary",
                "action_version": 1,
                "required_policy": ["workflow:execute"],
                "timeout_ms": 2000,
                "retry": {
                "max_attempts": 1,
                "delays_ms": []
                },
                "input_mapping": {},
                "input_constants": {
                "currency": "RUB",
                "enabled": true,
                "limit": 10
                }
            }
            },
            {
            "key": "done",
            "type": "end",
            "outcome": "COMPLETED"
            }
        ],
        "transitions": [
            {
            "from": "invoke",
            "outcome": "APPLIED",
            "to": "done"
            }
        ]
        }
        """;

        var map = WorkflowMapLoader.Load(json);

        var automatic = Assert.IsType<AutomaticStep>(map.Steps[0]);
        var constants = automatic.Task.InputConstants;

        Assert.Equal(JsonValueKind.Object, constants.ValueKind);
        Assert.Equal("RUB", constants.GetProperty("currency").GetString());
        Assert.True(constants.GetProperty("enabled").GetBoolean());
        Assert.Equal(10, constants.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void Load_AllStepTypes_DeserializesCorrectly()
    {
        const string json = """
        {
        "contract_version": "course-1",
        "flow_name": "types-test",
        "version": 1,
        "start_step": "automatic",
        "steps": [
            {
            "key": "automatic",
            "type": "automatic",
            "task": {
                "service": "postgres",
                "module": "training",
                "action": "canary",
                "action_version": 1,
                "required_policy": [],
                "timeout_ms": 1000,
                "retry": {
                "max_attempts": 1,
                "delays_ms": []
                },
                "input_mapping": {},
                "input_constants": {}
            }
            },
            {
            "key": "signal",
            "type": "wait_signal",
            "signal_type": "training.completed",
            "outcome": "RECEIVED"
            },
            {
            "key": "manual",
            "type": "manual",
            "allowed_outcomes": ["APPROVED"]
            },
            {
            "key": "end",
            "type": "end",
            "outcome": "COMPLETED"
            }
        ],
        "transitions": [
            {
            "from": "automatic",
            "outcome": "APPLIED",
            "to": "signal"
            },
            {
            "from": "signal",
            "outcome": "RECEIVED",
            "to": "manual"
            },
            {
            "from": "manual",
            "outcome": "APPROVED",
            "to": "end"
            }
        ]
        }
        """;

        var map = WorkflowMapLoader.Load(json);

        Assert.IsType<AutomaticStep>(map.Steps[0]);
        Assert.IsType<WaitSignalStep>(map.Steps[1]);
        Assert.IsType<ManualStep>(map.Steps[2]);
        Assert.IsType<EndStep>(map.Steps[3]);
    }

}