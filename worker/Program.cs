using ModuleDev.ActionRuntime;
using ModuleDev.Worker;
using ModuleDev.Worker.Persistence;
using ModuleDev.Workflow;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<IWorkflowJobStore>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var value = configuration["WORKER_LEASE_DURATION_MS"];

    if (!int.TryParse(value, out var milliseconds) || milliseconds <= 0)
    {
        throw new InvalidOperationException(
            "WORKER_LEASE_DURATION_MS must be a positive integer");
    }

    return new PostgresWorkflowJobStore(
        sp.GetRequiredService<DbConnectionFactory>(),
        TimeSpan.FromMilliseconds(milliseconds));
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
