using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Api.Tests.Helpers;

public sealed class CliActionHelper
{
    private readonly string _repositoryRoot;
    private readonly string _connectionString;
    private readonly Lazy<Task<string>> _cliDllPath;

    public CliActionHelper(
        string connectionString)
    {
        _repositoryRoot =
            FindRepositoryRoot();

        _connectionString =
            connectionString;

        _cliDllPath =
            new Lazy<Task<string>>(
                BuildCliAsync);
    }

    public async Task<CliResult> PublishAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            "action",
            "publish",
            manifestPath,
            cancellationToken);
    }

    public async Task<CliResult> ActivateAsync(
        string key,
        int version,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            "action",
            "activate",
            key,
            "--version",
            version.ToString(),
            cancellationToken);
    }

    public async Task<CliResult> DisableAsync(
        string key,
        int version,
        int? replacementVersion = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "action",
            "disable",
            key,
            "--version",
            version.ToString()
        };

        if (replacementVersion.HasValue)
        {
            arguments.Add("--replacement-version");
            arguments.Add(
                replacementVersion.Value.ToString());
        }

        return await RunAsync(
            arguments.ToArray(),
            cancellationToken);
    }

    public string CreateManifestFile(
        string module,
        string action,
        int version,
        bool enabled = false,
        bool isDefault = false,
        int timeoutMs = 5000)
    {
        var directory =
            Path.Combine(
                Path.GetTempPath(),
                "moduledev-api-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path =
            Path.Combine(
                directory,
                $"{module}-{action}-v{version}.json");

        var manifest =
            new JsonObject
            {
                ["contract_version"] = "course-1",
                ["module"] = module,
                ["action"] = action,
                ["version"] = version,
                ["http_method"] = "POST",
                ["target_schema"] = "test",
                ["target_function"] = "echo_v1",

                ["request_schema"] =
                    new JsonObject
                    {
                        ["$schema"] =
                            "https://json-schema.org/draft/2020-12/schema",

                        ["type"] = "object",

                        ["additionalProperties"] = false,

                        ["required"] =
                            new JsonArray("value"),

                        ["properties"] =
                            new JsonObject
                            {
                                ["value"] =
                                    new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["minLength"] = 1,
                                        ["maxLength"] = 128
                                    }
                            }
                    },

                ["response_schema"] =
                    new JsonObject
                    {
                        ["$schema"] =
                            "https://json-schema.org/draft/2020-12/schema",

                        ["type"] = "object",

                        ["additionalProperties"] = false,

                        ["required"] =
                            new JsonArray(
                                "echo",
                                "principal"),

                        ["properties"] =
                            new JsonObject
                            {
                                ["echo"] =
                                    new JsonObject
                                    {
                                        ["type"] = "object"
                                    },

                                ["principal"] =
                                    new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["minLength"] = 1
                                    }
                            }
                    },

                ["outcomes"] =
                    new JsonArray("OK"),

                ["required_policy"] =
                    new JsonArray(),

                ["idempotency_mode"] = "required",

                ["idempotency_scope"] =
                    "principal_action",

                ["timeout_ms"] = timeoutMs,

                ["enabled"] = enabled,

                ["is_default"] = isDefault
            };

        File.WriteAllText(
            path,
            manifest.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        return path;
    }

    public void DeleteManifestFile(
        string path)
    {
        var directory =
            Path.GetDirectoryName(path);

        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
        catch
        {
            // Do not hide the original test failure.
        }
    }

    private async Task<CliResult> RunAsync(
        string arg1,
        string arg2,
        string arg3,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            new[] { arg1, arg2, arg3 },
            cancellationToken);
    }

    private async Task<CliResult> RunAsync(
        string arg1,
        string arg2,
        string arg3,
        string arg4,
        string arg5,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            new[] { arg1, arg2, arg3, arg4, arg5 },
            cancellationToken);
    }

    private async Task<CliResult> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var cliDll =
            await _cliDllPath.Value;

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    OperatingSystem.IsWindows()
                        ? "dotnet.exe"
                        : "dotnet",

                WorkingDirectory =
                    _repositoryRoot,

                UseShellExecute = false,

                RedirectStandardOutput = true,
                RedirectStandardError = true,

                CreateNoWindow = true
            };

        startInfo.ArgumentList.Add(cliDll);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ConnectionStrings__Database"] =
            _connectionString;

        using var process =
            new Process
            {
                StartInfo = startInfo
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start CLI process.");
        }

        var stdoutTask =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);

        var stderrTask =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        var stdout =
            await stdoutTask;

        var stderr =
            await stderrTask;

        JsonDocument? document = null;

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            try
            {
                document =
                    JsonDocument.Parse(stdout);
            }
            catch (JsonException)
            {
                // Caller receives raw stdout/stderr.
            }
        }

        return new CliResult(
            process.ExitCode,
            stdout,
            stderr,
            document);
    }

    private async Task<string> BuildCliAsync()
    {
        var cliProject =
            Path.Combine(
                _repositoryRoot,
                "cli",
                "Cli.csproj");

        if (!File.Exists(cliProject))
        {
            throw new FileNotFoundException(
                "CLI project was not found.",
                cliProject);
        }

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    OperatingSystem.IsWindows()
                        ? "dotnet.exe"
                        : "dotnet",

                WorkingDirectory =
                    _repositoryRoot,

                UseShellExecute = false,

                RedirectStandardOutput = true,
                RedirectStandardError = true,

                CreateNoWindow = true
            };

        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(cliProject);
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Debug");

        using var process =
            new Process
            {
                StartInfo = startInfo
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to build CLI.");
        }

        var stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        var stderrTask =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout =
            await stdoutTask;

        var stderr =
            await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "CLI build failed." +
                Environment.NewLine +
                $"Exit code: {process.ExitCode}" +
                Environment.NewLine +
                $"STDOUT:{Environment.NewLine}{stdout}" +
                Environment.NewLine +
                $"STDERR:{Environment.NewLine}{stderr}");
        }

        var configurationDirectory =
            Path.Combine(
                _repositoryRoot,
                "cli",
                "bin",
                "Debug",
                "net10.0");

        var cliDll =
            Path.Combine(
                configurationDirectory,
                "Cli.dll");

        if (!File.Exists(cliDll))
        {
            throw new FileNotFoundException(
                "CLI assembly was not produced.",
                cliDll);
        }

        return cliDll;
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "ModuleDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing ModuleDev.slnx.");
    }

    public string CreateRollbackManifestFile(
        string module,
        string action,
        int version = 1)
    {
        var directory =
            Path.Combine(
                Path.GetTempPath(),
                "moduledev-api-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var path =
            Path.Combine(
                directory,
                $"{module}-{action}-v{version}.json");

        var manifest =
            new JsonObject
            {
                ["contract_version"] = "course-1",
                ["module"] = module,
                ["action"] = action,
                ["version"] = version,
                ["http_method"] = "POST",

                ["target_schema"] = "test",
                ["target_function"] = "echo_rollback_v1",

                ["request_schema"] =
                    new JsonObject
                    {
                        ["$schema"] =
                            "https://json-schema.org/draft/2020-12/schema",

                        ["type"] = "object",

                        ["additionalProperties"] = false,

                        ["required"] =
                            new JsonArray("marker"),

                        ["properties"] =
                            new JsonObject
                            {
                                ["marker"] =
                                    new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["minLength"] = 1,
                                        ["maxLength"] = 128
                                    }
                            }
                    },

                /*
                * Deliberately declares a different outcome.
                *
                * test.echo_rollback_v1 returns NOT_ALLOWED,
                * but manifest declares EXPECTED.
                *
                * Therefore response validation must reject the
                * target result and the whole transaction must roll back.
                */
                ["response_schema"] =
                    new JsonObject
                    {
                        ["$schema"] =
                            "https://json-schema.org/draft/2020-12/schema",

                        ["type"] = "object",

                        ["additionalProperties"] = false,

                        ["required"] =
                            new JsonArray(
                                "echo",
                                "principal"),

                        ["properties"] =
                            new JsonObject
                            {
                                ["echo"] =
                                    new JsonObject
                                    {
                                        ["type"] = "object"
                                    },

                                ["principal"] =
                                    new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["minLength"] = 1
                                    }
                            }
                    },

                ["outcomes"] =
                    new JsonArray("EXPECTED"),

                ["required_policy"] =
                    new JsonArray("workflow:execute"),

                ["idempotency_mode"] =
                    "required",

                ["idempotency_scope"] =
                    "principal_action",

                ["timeout_ms"] =
                    5000,

                ["enabled"] =
                    true,

                ["is_default"] =
                    true
            };

        File.WriteAllText(
            path,
            manifest.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        return path;
    }

}

public sealed record CliResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    JsonDocument? Document)
{
    public string? Status =>
        Document?
            .RootElement
            .TryGetProperty(
                "status",
                out var property) == true
            ? property.GetString()
            : null;

    public string? Code =>
        Document?
            .RootElement
            .TryGetProperty(
                "code",
                out var property) == true
            ? property.GetString()
            : null;
}