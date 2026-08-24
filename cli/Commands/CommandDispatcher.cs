using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;

namespace ModuleDev.Cli.Commands;

public sealed class CommandDispatcher
{
    private readonly MigrationService _migrations;
    private readonly ActionValidateCommand _actionValidate;
    private readonly ActionPublishCommand _actionPublish;
    private readonly ActionListCommand _actionList;
    private readonly CliOutput _output;

    public CommandDispatcher(
        MigrationService migrations,
        ActionValidateCommand actionValidate,
        ActionPublishCommand actionPublish,
        ActionListCommand actionList,
        CliOutput output)
    {
        _migrations = migrations;
        _actionValidate = actionValidate;
        _actionPublish = actionPublish;
        _actionList = actionList;
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

        if (IsCommand(args, "action", "validate"))
        {
            return await ExecuteActionValidateAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "action", "publish"))
        {
            return await ExecuteActionPublishAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "action", "list"))
        {
            return await ExecuteActionListAsync(
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
                code: "request.invalid",
                message: "usage: action validate <manifest>");
        }

        return await _actionValidate.ExecuteAsync(
            args[2],
            cancellationToken);
    }

    private async Task<int> ExecuteActionPublishAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: action publish <manifest>");
        }

        return await _actionPublish.ExecuteAsync(
            args[2],
            cancellationToken);
    }

    private async Task<int> ExecuteActionListAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: action list");
        }

        return await _actionList.ExecuteAsync(
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