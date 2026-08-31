namespace ModuleDev.Workflow;

public sealed class WorkflowMapValidationResult
{
    private WorkflowMapValidationResult(
        bool isValid,
        IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<string> Errors { get; }

    public static WorkflowMapValidationResult Success()
    {
        return new WorkflowMapValidationResult(
            true,
            Array.Empty<string>());
    }

    public static WorkflowMapValidationResult Failure(
        params string[] errors)
    {
        return new WorkflowMapValidationResult(
            false,
            errors);
    }

    public static WorkflowMapValidationResult Failure(
        IEnumerable<string> errors)
    {
        return new WorkflowMapValidationResult(
            false,
            errors.ToArray());
    }
}