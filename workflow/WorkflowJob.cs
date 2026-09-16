namespace ModuleDev.Workflow;

public sealed class WorkflowJob
{
    public required Guid JobId { get; init; }
    public required Guid ProcessId { get; init; }
    public required Guid StepInstanceId { get; init; }
    public required Guid ExecutionId { get; init; }
    public required Guid AttemptId { get; init; }
    public required int AttemptNumber { get; init; }
    public required long LeaseVersion { get; init; }

    public required string FlowName { get; init; }
    public required int FlowVersion { get; init; }
    public required string StepKey { get; init; }

    public required string Service { get; init; }
    public required string Module { get; init; }
    public required string Action { get; init; }
    public required int ActionVersion { get; init; }

    public required string RequestSchema { get; init; }
    public required string ResponseSchema { get; init; }
    public required string Outcomes { get; init; }
    public required string RequiredPolicy { get; init; }

    public required int TimeoutMs { get; init; }
    public required int MaxAttempts { get; init; }
    public required string DelaysMs { get; init; }
    public required string InputMapping { get; init; }
    public required string InputConstants { get; init; }
    public required string ProcessData { get; init; }
}
