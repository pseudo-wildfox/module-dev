using System.Data.Common;
using ModuleDev.Workflow;

namespace ModuleDev.Worker;

public class Worker : BackgroundService
{
    private readonly string _leaseOwner;
    private readonly IWorkflowJobStore _jobStore;
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger, IWorkflowJobStore jobStore, IConfiguration configuration)
    {
        _logger = logger;
        _jobStore = jobStore;
        _leaseOwner =
            configuration["WORKER_LEASE_OWNER"]
            ?? throw new InvalidOperationException("WORKER_LEASE_OWNER is required");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await _jobStore.ClaimJobsAsync(_leaseOwner, 1, stoppingToken);

                foreach (var job in jobs)
                {
                    LogClaimedJob(job);
                }
            }
            catch (DbException ex) when (ex.IsTransient)
            {
                _logger.LogWarning(ex, "Transient workflow store error. Retrying.");
            }

            await Task.Delay(100, stoppingToken);
        }
    }

    private void LogClaimedJob(WorkflowJob job)
    {
        _logger.LogInformation(
            """
            Claimed job {JobId}, attempt {AttemptId}, execution {ExecutionId}, step {StepKey},
            action {Module}/{Action} v{ActionVersion}, leaseVersion {LeaseVersion}
            """,
            job.JobId,
            job.AttemptId,
            job.ExecutionId,
            job.StepKey,
            job.Module,
            job.Action,
            job.ActionVersion,
            job.LeaseVersion
        );
    }
}
