using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Api.Tests.Helpers;

public sealed class CliActionHelper
{
    private readonly string _repositoryRoot;
    private readonly string _connectionString;

    public CliActionHelper(
        string repositoryRoot,
        string connectionString)
    {
        _repositoryRoot = repositoryRoot;
        _connectionString = connectionString;
    }

    public async Task<CliResult> PublishAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            [
                "action",
                "publish",
                manifestPath
            ],
            cancellationToken);
    }

    public async Task<CliResult> ActivateAsync(
        string key,
        int version,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            [
                "action",
                "activate",
                key,
                "--version",
                version.ToString()
            ],
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
                ["idempotency_scope"] = "principal_action",
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
            // Test cleanup should not hide the
            // original assertion failure.
        }
    }

    private async Task<CliResult> RunAsync(
        string arg1,
        string arg2,
        string arg3,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            new[] { arg1, arg2, arg3 },
            cancellationToken);
    }

    private async Task<CliResult> RunAsync(
        params string[] arguments)
    {
        return await RunAsync(
            arguments,
            CancellationToken.None);
    }

    private async Task<CliResult> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken)
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

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(cliProject);
        startInfo.ArgumentList.Add("--");

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
                "Failed to start CLI.");
        }

        var stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        var stderrTask =
            process.StandardError.ReadToEndAsync();

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
                // The caller can inspect raw stdout.
            }
        }

        return new CliResult(
            process.ExitCode,
            stdout,
            stderr,
            document);
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