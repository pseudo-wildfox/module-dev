using System.Text.Json;
using Json.Schema;

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
        var buildOptions = new BuildOptions
        {
            SchemaRegistry = new SchemaRegistry()
        };

        var jsonSchema = JsonSchema.FromText(
            schema.GetRawText(),
            buildOptions);

        var result = jsonSchema.Evaluate(
            payload,
            _options);

        return result.IsValid;
    }
}