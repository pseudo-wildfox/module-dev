using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;

namespace ModuleDev.Cli.Commands;

public sealed class CommandDispatcher
{
    private readonly MigrationService _migrations;
    private readonly ActionValidateCommand _actionValidate;
    private readonly ActionPublishCommand _actionPublish;
    private readonly ActionListCommand _actionList;
    private readonly ActionActivateCommand _actionActivate;
    private readonly ActionDisableCommand _actionDisable;

    private readonly FlowValidateCommand _flowValidate;
    private readonly FlowPublishCommand _flowPublish;
    private readonly FlowListCommand _flowList;
    private readonly FlowActivateCommand _flowActivate;
    private readonly FlowStartCommand _flowStart;
    private readonly FlowGetCommand _flowGet;
    private readonly FlowSignalCommand _flowSignal;

    private readonly CliOutput _output;

    public CommandDispatcher(
        MigrationService migrations,
        ActionValidateCommand actionValidate,
        ActionPublishCommand actionPublish,
        ActionListCommand actionList,
        ActionActivateCommand actionActivate,
        ActionDisableCommand actionDisable,
        FlowValidateCommand flowValidate,
        FlowPublishCommand flowPublish,
        FlowListCommand flowList,
        FlowActivateCommand flowActivate,
        FlowStartCommand flowStart,
        FlowGetCommand flowGet,
        FlowSignalCommand flowSignal,
        CliOutput output)
    {
        _migrations = migrations;
        _actionValidate = actionValidate;
        _actionPublish = actionPublish;
        _actionList = actionList;
        _actionActivate = actionActivate;
        _actionDisable = actionDisable;
        _flowValidate = flowValidate;
        _flowPublish = flowPublish;
        _flowList = flowList;
        _flowActivate = flowActivate;
        _flowStart = flowStart;
        _flowGet = flowGet;
        _flowSignal = flowSignal;
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

        if (IsCommand(args, "action", "activate"))
        {
            return await ExecuteActionActivateAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "action", "disable"))
        {
            return await ExecuteActionDisableAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "flow", "validate"))
        {
            return await ExecuteFlowValidateAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "flow", "publish"))
        {
            return await ExecuteFlowPublishAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "flow", "list"))
        {
            return await ExecuteFlowListAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "flow", "activate"))
        {
            return await ExecuteFlowActivateAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "flow", "start"))
        {
            return await ExecuteFlowStartAsync(
                args,
                cancellationToken);
        }

        if (IsCommand(args, "flow", "get"))
        {
            return await ExecuteFlowGetAsync(
                args,
                cancellationToken);
        }       

        if (IsCommand(args, "flow", "signal"))
        {
            return await ExecuteFlowSignalAsync(
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

    private async Task<int> ExecuteActionActivateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 5 ||
            !args[3].Equals(
                "--version",
                StringComparison.OrdinalIgnoreCase))
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "usage: action activate <module.action> --version <version>");
        }

        if (!int.TryParse(
                args[4],
                out var version))
        {
            return _output.Error(
                code: "request.invalid",
                message: "version must be an integer");
        }

        return await _actionActivate.ExecuteAsync(
            args[2],
            version,
            cancellationToken);
    }

    private async Task<int> ExecuteActionDisableAsync(
    string[] args,
    CancellationToken cancellationToken)
    {
        if (args.Length is not (5 or 7))
        {
            return _output.Error(
                code: "request.invalid",
                message:
                "usage: action disable <module.action> --version <version> [--replacement-version <version>]");
        }

        var actionKey = args[2];

        if (!args[3].Equals(
                "--version",
                StringComparison.OrdinalIgnoreCase))
        {
            return _output.Error(
                code: "request.invalid",
                message:
                "usage: action disable <module.action> --version <version> [--replacement-version <version>]");
        }

        if (!int.TryParse(args[4], out var version))
        {
            return _output.Error(
                code: "request.invalid",
                message: "version must be an integer");
        }

        int? replacementVersion = null;

        if (args.Length > 5)
        {
            if (args.Length != 7
                || !args[5].Equals(
                    "--replacement-version",
                    StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(
                    args[6],
                    out var parsedReplacementVersion))
            {
                return _output.Error(
                    code: "request.invalid",
                    message:
                    "usage: action disable <module.action> --version <version> [--replacement-version <version>]");
            }

            replacementVersion = parsedReplacementVersion;
        }

        return await _actionDisable.ExecuteAsync(
            actionKey,
            version,
            replacementVersion,
            cancellationToken);
    }

    private async Task<int> ExecuteFlowValidateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: flow validate <file>");
        }

        return await _flowValidate.ExecuteAsync(
            args[2],
            cancellationToken);
    }

    private async Task<int> ExecuteFlowPublishAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: flow publish <file>");
        }

        return await _flowPublish.ExecuteAsync(
            args[2],
            cancellationToken);
    }


    private async Task<int> ExecuteFlowListAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: flow list");
        }

        return await _flowList.ExecuteAsync(
            cancellationToken);
    }

    private async Task<int> ExecuteFlowActivateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 5 ||
            !args[3].Equals(
                "--version",
                StringComparison.OrdinalIgnoreCase))
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "usage: flow activate <flow> --version <version>");
        }

        if (!int.TryParse(
                args[4],
                out var version))
        {
            return _output.Error(
                code: "request.invalid",
                message: "version must be an integer");
        }

        return await _flowActivate.ExecuteAsync(
            args[2],
            version,
            cancellationToken);
    }

private async Task<int> ExecuteFlowStartAsync(
    string[] args,
    CancellationToken cancellationToken)
{
    if (args.Length != 5 && args.Length != 7)
    {
        return _output.Error(
            code: "request.invalid",
            message:
                "usage: flow start <flow> --business-key <key> [--data <file>]");
    }

    if (!args[3].Equals(
            "--business-key",
            StringComparison.OrdinalIgnoreCase))
    {
        return _output.Error(
            code: "request.invalid",
            message:
                "usage: flow start <flow> --business-key <key> [--data <file>]");
    }

    var flowName = args[2];
    var businessKey = args[4];

    string? dataPath = null;

    if (args.Length == 7)
    {
        if (!args[5].Equals(
                "--data",
                StringComparison.OrdinalIgnoreCase))
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "usage: flow start <flow> --business-key <key> [--data <file>]");
        }

        dataPath = args[6];
    }

    return await _flowStart.ExecuteAsync(
        flowName,
        businessKey,
        dataPath,
        cancellationToken);
}

    private async Task<int> ExecuteFlowGetAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            return _output.Error(
                code: "request.invalid",
                message: "usage: flow get <process-id>");
        }

        if (!Guid.TryParse(
                args[2],
                out var processId))
        {
            return _output.Error(
                code: "request.invalid",
                message: "process-id must be a valid UUID");
        }

        return await _flowGet.ExecuteAsync(
            processId,
            cancellationToken);
    }


    private async Task<int> ExecuteFlowSignalAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 9)
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "usage: flow signal <process-id> --type <type> --message-id <id> --payload <file>");
        }

        if (!Guid.TryParse(
                args[2],
                out var processId))
        {
            return _output.Error(
                code: "request.invalid",
                message: "process-id must be a valid UUID");
        }

        if (!args[3].Equals(
                "--type",
                StringComparison.OrdinalIgnoreCase)
            ||
            !args[5].Equals(
                "--message-id",
                StringComparison.OrdinalIgnoreCase)
            ||
            !args[7].Equals(
                "--payload",
                StringComparison.OrdinalIgnoreCase))
        {
            return _output.Error(
                code: "request.invalid",
                message:
                    "usage: flow signal <process-id> --type <type> --message-id <id> --payload <file>");
        }

        return await _flowSignal.ExecuteAsync(
            processId,
            args[4],
            args[6],
            args[8],
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