using System.Diagnostics;
using System.Text.Json;

namespace Workflow.Tests.Helpers;

public sealed class WorkflowTestCli
{
    private readonly string _repositoryRoot;
    private readonly string _cliProject;

    private readonly string _databaseHost;
    private readonly int _databasePort;
    private readonly string _databaseName;
    private readonly string _databaseUser;
    private readonly string _databasePassword;
    private readonly string _publisherUsername;
    private readonly string _publisherPassword;

    public WorkflowTestCli(
        string repositoryRoot,
        string databaseHost,
        int databasePort,
        string databaseName,
        string databaseUser,
        string databasePassword,
        string publisherUsername,
        string publisherPassword
    )
    {
        _repositoryRoot = repositoryRoot;
        _cliProject = Path.Combine(repositoryRoot, "cli", "Cli.csproj");

        _databaseHost = databaseHost;
        _databasePort = databasePort;
        _databaseName = databaseName;
        _databaseUser = databaseUser;
        _databasePassword = databasePassword;
        _publisherUsername = publisherUsername;
        _publisherPassword = publisherPassword;
    }

    public async Task<JsonDocument> RunAsync(params string[] arguments)
    {
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
        startInfo.ArgumentList.Add(_cliProject);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DB_HOST"] = _databaseHost;
        startInfo.Environment["DB_PORT"] = _databasePort.ToString();
        startInfo.Environment["DB_NAME"] = _databaseName;
        startInfo.Environment["DB_USER"] = _databaseUser;
        startInfo.Environment["DB_PASSWORD"] = _databasePassword;
        startInfo.Environment["DB_PUBLISHER_USER"] = _publisherUsername;
        startInfo.Environment["DB_PUBLISHER_PASSWORD"] = _publisherPassword;

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start CLI process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();

        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"CLI command failed.{Environment.NewLine}"
                    + $"Exit code: {process.ExitCode}{Environment.NewLine}"
                    + $"STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}"
                    + $"STDERR:{Environment.NewLine}{stderr}"
            );
        }

        return JsonDocument.Parse(stdout);
    }
}
