using System.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using Workflow.Tests.Helpers;

namespace Workflow.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly string _repositoryRoot;
    private readonly PostgreSqlContainer _container;

    public string RepositoryRoot => _repositoryRoot;

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(5432);

    public string Database => "course";

    public string Username => "postgres";

    public string Password => "postgres";

    public string PublisherUsername => "course_publisher";

    public string PublisherPassword => "test-publisher-password";

    public PostgreSqlFixture()
    {
        _repositoryRoot = FindRepositoryRoot();

        var bootstrapDirectory = Path.Combine(_repositoryRoot, "db", "bootstrap");

        _container = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("course")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithEnvironment("COURSE_MIGRATOR_PASSWORD", "test-migrator-password")
            .WithEnvironment("COURSE_PUBLISHER_PASSWORD", "test-publisher-password")
            .WithEnvironment("COURSE_RUNTIME_PASSWORD", "test-runtime-password")
            .WithBindMount(bootstrapDirectory, "/docker-entrypoint-initdb.d")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await WaitForPostgreSqlAsync();

        await RunCliMigrationAsync();

        await PrepareWorkflowSmokeAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public NpgsqlConnection CreateConnection() => new(ConnectionString);

    private async Task WaitForPostgreSqlAsync()
    {
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);

                await connection.OpenAsync();

                return;
            }
            catch when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new InvalidOperationException("PostgreSQL test container did not become ready.");
    }

    private async Task RunCliMigrationAsync()
    {
        var cliProject = Path.Combine(_repositoryRoot, "cli", "Cli.csproj");

        var migrationsDirectory = Path.Combine(_repositoryRoot, "db", "migrations");

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",

            WorkingDirectory = _repositoryRoot,

            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(cliProject);
        startInfo.ArgumentList.Add("--");

        startInfo.ArgumentList.Add("migration");
        startInfo.ArgumentList.Add("apply");

        startInfo.ArgumentList.Add(Path.GetRelativePath(_repositoryRoot, migrationsDirectory));

        startInfo.Environment["DB_HOST"] = _container.Hostname;

        startInfo.Environment["DB_PORT"] = _container.GetMappedPublicPort(5432).ToString();

        startInfo.Environment["DB_NAME"] = "course";

        startInfo.Environment["DB_USER"] = "postgres";

        startInfo.Environment["DB_PASSWORD"] = "postgres";

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start CLI migration process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();

        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;

        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "CLI migration failed."
                    + Environment.NewLine
                    + $"Exit code: {process.ExitCode}"
                    + Environment.NewLine
                    + $"STDOUT:{Environment.NewLine}{stdout}"
                    + Environment.NewLine
                    + $"STDERR:{Environment.NewLine}{stderr}"
            );
        }
    }

    private async Task PrepareWorkflowSmokeAsync()
    {
        var cli = CreateCli();

        await cli.RunAsync("action", "activate", "training.canary", "--version", "1");

        var workflowMap = Path.Combine(
            _repositoryRoot,
            "contracts",
            "course-1",
            "workflow-map.example.json"
        );

        await cli.RunAsync("flow", "publish", workflowMap);

        await cli.RunAsync("flow", "activate", "workflow-smoke", "--version", "1");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (
                File.Exists(Path.Combine(directory.FullName, "ModuleDev.slnx"))
                && Directory.Exists(Path.Combine(directory.FullName, "db"))
                && Directory.Exists(Path.Combine(directory.FullName, "cli"))
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    public WorkflowTestCli CreateCli()
    {
        return new WorkflowTestCli(
            _repositoryRoot,
            Host,
            Port,
            Database,
            Username,
            Password,
            PublisherUsername,
            PublisherPassword
        );
    }

    public async Task ResetWorkflowStateAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);

        await connection.OpenAsync();

        const string sql = """
            TRUNCATE TABLE
                workflow.workflow_event,
                workflow.workflow_signal,
                workflow.task_attempt,
                workflow.workflow_job,
                workflow.step_instance,
                workflow.process_instance
            RESTART IDENTITY;
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
