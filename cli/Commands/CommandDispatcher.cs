using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;

namespace ModuleDev.Cli.Commands;

public sealed class CommandDispatcher
{
    private readonly MigrationService _migrations;
    private readonly ActionValidateCommand _actionValidate;
    private readonly CliOutput _output;


    public CommandDispatcher(
        MigrationService migrations,
        ActionValidateCommand actionValidate,
        CliOutput output)
    {
        _migrations = migrations;
        _actionValidate = actionValidate;
        _output = output;
    }


    public async Task<int> ExecuteAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            return _output.Error(
                "request.invalid",
                "command is required");
        }


        if (IsCommand(args, "migration", "apply"))
        {
            return await ExecuteMigrationApplyAsync(
                args,
                cancellationToken);
        }


        if (IsCommand(args, "action", "validate"))
        {
            return await ExecuteActionValidateAsync(
                args,
                cancellationToken);
        }


        return _output.Error(
            "request.invalid",
            "unsupported command");
    }



    private async Task<int> ExecuteMigrationApplyAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                "request.invalid",
                "usage: migration apply <directory>");
        }


        return await _migrations.ApplyAsync(
            args[2],
            cancellationToken);
    }



    private async Task<int> ExecuteActionValidateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                "request.invalid",
                "usage: action validate <manifest>");
        }


        return await _actionValidate.ExecuteAsync(
            args[2],
            cancellationToken);
    }



    private static bool IsCommand(
        string[] args,
        string first,
        string second)
    {
        return args.Length >= 2
            &&
            args[0].Equals(
                first,
                StringComparison.OrdinalIgnoreCase)
            &&
            args[1].Equals(
                second,
                StringComparison.OrdinalIgnoreCase);
    }
}