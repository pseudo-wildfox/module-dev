using ModuleDev.Cli.Output;
using ModuleDev.Workflow;

namespace ModuleDev.Cli.Commands;

public sealed class FlowValidateCommand
{
    private readonly WorkflowMapValidator _validator;
    private readonly CliOutput _output;

    public FlowValidateCommand(
        WorkflowMapValidator validator,
        CliOutput output)
    {
        _validator = validator;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string mapPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mapPath))
        {
            return _output.Error(
                code: "request.invalid",
                message: "workflow map file not found");
        }

        string mapJson;

        try
        {
            mapJson = await File.ReadAllTextAsync(
                mapPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "request.invalid",
                message: "failed to read workflow map file");
        }

        WorkflowMapValidationResult validation;

        try
        {
            validation = await _validator.ValidateAsync(
                mapJson,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteDiagnostic(ex);

            return _output.Error(
                code: "manifest.invalid",
                message: "workflow map validation failed");
        }

        if (!validation.IsValid)
        {
            return _output.Error(
                code: "manifest.invalid",
                message: "workflow map is invalid",
                details: new
                {
                    errors = validation.Errors
                });
        }

        return _output.Success(
            new
            {
                resource = "flow",
                operation = "validated"
            });
    }
}