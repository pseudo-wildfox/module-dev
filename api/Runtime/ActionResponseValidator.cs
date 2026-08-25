using System.Text.Json;
using Json.Schema;
using ModuleDev.Contracts;

namespace ModuleDev.Api.Runtime;

public sealed class ActionResponseValidator
{
    private readonly EvaluationOptions _options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    public ResponseValidationResult Validate(
        ActionManifest manifest,
        JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            return ResponseValidationResult.Failure(
                "action response must be a JSON object.");
        }

        if (!response.TryGetProperty(
                "status",
                out var status) ||
            status.ValueKind != JsonValueKind.String)
        {
            return ResponseValidationResult.Failure(
                "action response status is missing.");
        }

        if (!string.Equals(
                status.GetString(),
                "ok",
                StringComparison.Ordinal))
        {
            return ResponseValidationResult.Failure(
                "action response status is not ok.");
        }

        if (!response.TryGetProperty(
                "outcome",
                out var outcome) ||
            outcome.ValueKind != JsonValueKind.String)
        {
            return ResponseValidationResult.Failure(
                "action response outcome is missing.");
        }

        var outcomeValue = outcome.GetString();

        if (string.IsNullOrWhiteSpace(outcomeValue) ||
            !manifest.Outcomes.Contains(
                outcomeValue,
                StringComparer.Ordinal))
        {
            return ResponseValidationResult.Failure(
                $"action response outcome '{outcomeValue}' " +
                "is not declared by the manifest.");
        }

        if (!response.TryGetProperty(
                "result",
                out var result))
        {
            return ResponseValidationResult.Failure(
                "action response result is missing.");
        }

        JsonSchema schema;

        try
        {
            schema = JsonSchema.FromText(
                manifest.ResponseSchema.GetRawText());
        }
        catch (Exception ex)
        {
            return ResponseValidationResult.Failure(
                "action response schema is invalid: " +
                ex.Message);
        }

        var evaluation = schema.Evaluate(
            result,
            _options);

        if (!evaluation.IsValid)
        {
            var details = new List<string>();

            CollectErrors(
                evaluation,
                details);

            return ResponseValidationResult.Failure(
                "action result does not match response schema. " +
                string.Join(" | ", details));
        }

        return ResponseValidationResult.Success();
    }

    private static void CollectErrors(
        EvaluationResults evaluation,
        ICollection<string> errors)
    {
        if (evaluation.Errors is not null)
        {
            foreach (var error in evaluation.Errors)
            {
                errors.Add(
                    $"{evaluation.InstanceLocation}: " +
                    $"{error.Key}: {error.Value}");
            }
        }

        if (evaluation.Details is null)
        {
            return;
        }

        foreach (var detail in evaluation.Details)
        {
            CollectErrors(detail, errors);
        }
    }
}

public sealed record ResponseValidationResult(
    bool IsValid,
    string? ErrorMessage)
{
    public static ResponseValidationResult Success()
        => new(true, null);

    public static ResponseValidationResult Failure(
        string message)
        => new(false, message);
}