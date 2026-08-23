using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;

namespace ModuleDev.Cli.Commands;

public sealed class CommandDispatcher
{
    private readonly MigrationService _migrations;
    private readonly CliOutput _output;

    public CommandDispatcher(
        MigrationService migrations,
        CliOutput output)
    {
        _migrations = migrations;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            return _output.Error(
                code: "request.invalid",
                message: "command is required");
        }

        if (IsCommand(args, "migration", "apply"))
        {
            return await ExecuteMigrationApplyAsync(
                args,
                cancellationToken);
        }

        return _output.Error(
            code: "request.invalid",
            message: "unsupported command");
    }

    private async Task<int> ExecuteMigrationApplyAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: migration apply <directory>");
        }

        var directory = args[2];

        return await _migrations.ApplyAsync(
            directory,
            cancellationToken);
    }

    private static bool IsCommand(
        string[] args,
        string first,
        string second)
    {
        return args.Length >= 2
               && args[0].Equals(
                   first,
                   StringComparison.OrdinalIgnoreCase)
               && args[1].Equals(
                   second,
                   StringComparison.OrdinalIgnoreCase);
    }
}