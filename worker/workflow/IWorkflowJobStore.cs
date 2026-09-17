namespace ModuleDev.Workflow;

public interface IWorkflowJobStore
{
    Task<IReadOnlyList<WorkflowJob>> ClaimJobsAsync(
        string leaseOwner,
        int limit,
        CancellationToken cancellationToken
    );
}
