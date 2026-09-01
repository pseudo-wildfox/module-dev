using System.Text.Json;

namespace Workflow.Tests.Builders;

public sealed class WorkflowMapBuilder
{
    private readonly Dictionary<string, object> _map;

    private readonly List<object> _steps = [];
    private readonly List<object> _transitions = [];

    private object? _automaticTask;

    public WorkflowMapBuilder()
    {
        _map = new()
        {
            ["contract_version"] = "course-1",
            ["flow_name"] = "test-flow",
            ["version"] = 1,
            ["start_step"] = "start",
        };

    }


    public WorkflowMapBuilder AddAutomatic(
        string key = "start")
    {
        _automaticTask = new
        {
            service = "postgres",
            module = "training",
            action = "canary",
            action_version = 1,
            required_policy =
                new[] { "workflow:execute" },
            timeout_ms = 2000,
            retry = new
            {
                max_attempts = 3,
                delays_ms = new[] { 100, 200 }
            },
            input_mapping = new Dictionary<string, string>
            {
                ["/value"] = "/value"
            },
            input_constants = new { }
        };


        _steps.Add(new
        {
            key,
            type = "automatic",
            task = _automaticTask
        });

        return this;
    }


    public WorkflowMapBuilder AddWaitSignal(
        string key)
    {
        _steps.Add(new
        {
            key,
            type = "wait_signal",
            signal_type = "training.completed",
            outcome = "RECEIVED"
        });

        return this;
    }


    public WorkflowMapBuilder AddManual(
        string key,
        string outcome)
    {
        _steps.Add(new
        {
            key,
            type = "manual",
            allowed_outcomes =
                new[] { outcome }
        });

        return this;
    }


    public WorkflowMapBuilder AddEnd(
        string key)
    {
        _steps.Add(new
        {
            key,
            type = "end",
            outcome = "COMPLETED"
        });

        return this;
    }


    public WorkflowMapBuilder RemoveEnd()
    {
        _steps.RemoveAll(x =>
        {
            var prop = x.GetType()
                .GetProperty("type");

            return prop?.GetValue(x)?.ToString()
                == "end";
        });

        return this;
    }


    public WorkflowMapBuilder DuplicateStep(
        string key)
    {
        _steps.Add(new
        {
            key,
            type = "end",
            outcome = "COMPLETED"
        });

        return this;
    }


    public WorkflowMapBuilder Transition(
        string from,
        string outcome,
        string to)
    {
        _transitions.Add(new
        {
            from,
            outcome,
            to
        });

        return this;
    }

    public WorkflowMapBuilder RemoveStart()
    {
        _steps.RemoveAll(step =>
        {
            var key =
                step.GetType()
                    .GetProperty("key")
                    ?.GetValue(step)
                    ?.ToString();

            return key == "start";
        });


        return this;
    }

    public WorkflowMapBuilder WithUnknownAction()
    {
        ReplaceAutomaticTask(task =>
        {
            return new
            {
                service = "postgres",
                module = "training",
                action = "missing_action",
                action_version = 1,
                required_policy =
                    new[] { "workflow:execute" },
                timeout_ms = 2000,
                retry = new
                {
                    max_attempts = 3,
                    delays_ms = new[] { 100, 200 }
                },
                input_mapping = new Dictionary<string, string>
                {
                    ["/value"] = "/value"
                },
                input_constants = new { }
            };
        });

        return this;
    }

    public WorkflowMapBuilder WithPolicyMismatch()
    {
        ReplaceAutomaticTask(task =>
        {
            return new
            {
                service = "postgres",
                module = "training",
                action = "canary",
                action_version = 1,
                required_policy =
                    new[]
                    {
                        "workflow:admin"
                    },
                timeout_ms = 2000,
                retry = new
                {
                    max_attempts = 3,
                    delays_ms = new[] { 100, 200 }
                },
                input_mapping = new Dictionary<string, string>
                {
                    ["/value"] = "/value"
                },
                input_constants = new { }
            };
        });

        return this;
    }

    public WorkflowMapBuilder WithPolicy(
        params string[] policy)
    {
        ReplaceAutomaticTask(task =>
        {
            return new
            {
                service = "postgres",
                module = "training",
                action = "canary",
                action_version = 1,

                required_policy = policy,

                timeout_ms = 2000,

                retry = new
                {
                    max_attempts = 3,
                    delays_ms = new[] { 100, 200 }
                },

                input_mapping =
                    new Dictionary<string, string>
                    {
                        ["/value"] = "/value"
                    },

                input_constants = new { }
            };
        });

        return this;
    }

    public WorkflowMapBuilder WithBadRetry()
    {
        ReplaceAutomaticTask(task =>
        {
            return new
            {
                service = "postgres",
                module = "training",
                action = "canary",
                action_version = 1,
                required_policy =
                    new[] { "workflow:execute" },
                timeout_ms = 2000,
                retry = new
                {
                    max_attempts = 3,
                    delays_ms = new[] { 100 }
                },
                input_mapping = new Dictionary<string, string>
                {
                    ["/value"] = "/value"
                },
                input_constants = new { }
            };
        });

        return this;
    }

    public WorkflowMapBuilder WithTimeout(
        int timeout)
    {
        ReplaceAutomaticTask(task =>
        {
            return new
            {
                service = "postgres",
                module = "training",
                action = "canary",
                action_version = 1,
                required_policy =
                    new[] { "workflow:execute" },
                timeout_ms = timeout,
                retry = new
                {
                    max_attempts = 3,
                    delays_ms = new[] { 100, 200 }
                },
                input_mapping = new Dictionary<string, string>
                {
                    ["/value"] = "/value"
                },
                input_constants = new { }
            };
        });

        return this;
    }

    public WorkflowMapBuilder WithInvalidMapping()
    {
        ReplaceAutomaticTask(task =>
        {
            return new
            {
                service = "postgres",
                module = "training",
                action = "canary",
                action_version = 1,
                required_policy =
                    new[] { "workflow:execute" },
                timeout_ms = 2000,
                retry = new
                {
                    max_attempts = 3,
                    delays_ms = new[] { 100, 200 }
                },
                input_mapping = new Dictionary<string, string>
                {
                    ["value"] = "value"
                },
                input_constants = new { }
            };
        });

        return this;
    }

    private void ReplaceAutomaticTask(
        Func<object, object> factory)
    {
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];

            var type =
                step.GetType()
                    .GetProperty("type")
                    ?.GetValue(step)
                    ?.ToString();

            if (type != "automatic")
            {
                continue;
            }

            var key =
                step.GetType()
                    .GetProperty("key")
                    ?.GetValue(step)
                    ?.ToString();


            _steps[i] = new
            {
                key,
                type = "automatic",
                task = factory(step)
            };

            return;
        }
    }

    public WorkflowMapBuilder AddAutomaticWithoutTransitions()
    {
        AddAutomatic();

        _transitions.Clear();

        return this;
    }


    public string Build()
    {
        _map["steps"] = _steps;
        _map["transitions"] = _transitions;

        return JsonSerializer.Serialize(_map);
    }
}