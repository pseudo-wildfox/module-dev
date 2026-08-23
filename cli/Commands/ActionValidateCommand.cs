using ModuleDev.Cli.Output;
using ModuleDev.Contracts.Validation;

namespace ModuleDev.Cli.Commands;

public sealed class ActionValidateCommand
{
    private readonly ActionManifestValidator _validator;
    private readonly CliOutput _output;


    public ActionValidateCommand(
        ActionManifestValidator validator,
        CliOutput output)
    {
        _validator = validator;
        _output = output;
    }


    public async Task<int> ExecuteAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            return _output.Error(
                code: "request.invalid",
                message: "manifest file not found");
        }


        var manifestJson =
            await File.ReadAllTextAsync(
                manifestPath,
                cancellationToken);


        var validation =
            _validator.Validate(manifestJson);


        if (!validation.IsValid)
        {
            return _output.Error(
                code: "manifest.invalid",
                message: "manifest does not match schema",
                details: new
                {
                    errors = validation.Errors
                });
        }


        return _output.Success(
            new
            {
                resource = "action",
                operation = "validated"
            });
    }
}