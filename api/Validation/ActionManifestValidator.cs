using System.Text.Json;
using Json.Schema;

namespace ModuleDev.Api.Validation;

public sealed class ActionManifestValidator
{
    private readonly JsonSchema _schema;

    private readonly EvaluationOptions _options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    public ActionManifestValidator(string schemaJson)
    {
        _schema = JsonSchema.FromText(schemaJson);
    }

    public ManifestValidationResult Validate(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            var result = _schema.Evaluate(
                document.RootElement,
                _options);

            if (result.IsValid)
            {
                return ManifestValidationResult.Success();
            }

            return ManifestValidationResult.Failure(
                "Manifest does not match the course-1 action manifest schema.");
        }
        catch (JsonException ex)
        {
            return ManifestValidationResult.Failure(
                $"Invalid JSON: {ex.Message}");
        }
    }
}