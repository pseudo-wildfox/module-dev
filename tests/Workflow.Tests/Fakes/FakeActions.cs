using ModuleDev.Contracts;

namespace Workflow.Tests.Fakes;

public static class FakeActions
{
    public static ActionManifest Canary()
    {
        return new ActionManifest
        {
            ContractVersion = "course-1",
            Module = "training",
            Action = "canary",
            Version = 1,

            HttpMethod = "POST",

            TargetSchema = "training",
            TargetFunction = "canary_v1",

            RequestSchema = default,
            ResponseSchema = default,

            Outcomes =
            [
                "APPLIED"
            ],

            RequiredPolicy =
            [
                "workflow:execute"
            ],

            IdempotencyMode = "required",
            IdempotencyScope = "principal_action",

            TimeoutMs = 2000,

            Enabled = true,
            IsDefault = true
        };
    }

    public static ActionManifest DisabledCanary()
    {
        var action = Canary();

        return new ActionManifest
        {
            ContractVersion = action.ContractVersion,
            Module = action.Module,
            Action = action.Action,
            Version = action.Version,

            HttpMethod = action.HttpMethod,

            TargetSchema = action.TargetSchema,
            TargetFunction = action.TargetFunction,

            RequestSchema = action.RequestSchema,
            ResponseSchema = action.ResponseSchema,

            Outcomes = action.Outcomes,
            RequiredPolicy = action.RequiredPolicy,

            IdempotencyMode = action.IdempotencyMode,
            IdempotencyScope = action.IdempotencyScope,

            TimeoutMs = action.TimeoutMs,

            Enabled = false,
            IsDefault = false
        };
    }
}