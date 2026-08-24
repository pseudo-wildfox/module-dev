using Json.Schema;
using System.Text.Json;

namespace ModuleDev.Api.Runtime;

public sealed class ActionRequestValidator
{
    private readonly EvaluationOptions _options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    public bool IsValid(
        JsonElement schema,
        JsonElement payload)
    {
        var jsonSchema = JsonSchema.FromText(
            schema.GetRawText());

        var result = jsonSchema.Evaluate(
            payload,
            _options);

        return result.IsValid;
    }
}