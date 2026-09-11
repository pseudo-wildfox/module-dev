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

        return status.GetString() switch
        {
            "ok" => ValidateSuccessResponse(
                manifest,
                response),

            "error" => ValidateErrorResponse(
                response),

            _ => ResponseValidationResult.Failure(
                "action response status is invalid.")
        };
    }

    private ResponseValidationResult ValidateSuccessResponse(
        ActionManifest manifest,
        JsonElement response)
    {
        var envelopeValidation =
            ValidateSuccessEnvelope(response);

        if (!envelopeValidation.IsValid)
        {
            return envelopeValidation;
        }

        var outcome =
            response.GetProperty("outcome");

        var outcomeValue =
            outcome.GetString();

        if (string.IsNullOrWhiteSpace(outcomeValue) ||
            !manifest.Outcomes.Contains(
                outcomeValue,
                StringComparer.Ordinal))
        {
            return ResponseValidationResult.Failure(
                $"action response outcome '{outcomeValue}' " +
                "is not declared by the manifest.");
        }

        var result =
            response.GetProperty("result");

        JsonSchema schema;

        try
        {
            var buildOptions = new BuildOptions
            {
                SchemaRegistry = new SchemaRegistry()
            };

            schema = JsonSchema.FromText(
                manifest.ResponseSchema.GetRawText(),
                buildOptions);
        }
        catch (Exception ex)
        {
            return ResponseValidationResult.Failure(
                "action response schema is invalid: " +
                ex.Message);
        }

        var evaluation =
            schema.Evaluate(
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

    private static ResponseValidationResult ValidateSuccessEnvelope(
        JsonElement response)
    {
        var allowedFields =
            new HashSet<string>(
                new[]
                {
                    "status",
                    "outcome",
                    "result",
                    "meta"
                },
                StringComparer.Ordinal);

        foreach (var property in response.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                return ResponseValidationResult.Failure(
                    $"action success response contains " +
                    $"undeclared field '{property.Name}'.");
            }
        }

        if (!response.TryGetProperty(
                "outcome",
                out var outcome) ||
            outcome.ValueKind != JsonValueKind.String)
        {
            return ResponseValidationResult.Failure(
                "action response outcome is missing.");
        }

        if (string.IsNullOrWhiteSpace(outcome.GetString()))
        {
            return ResponseValidationResult.Failure(
                "action response outcome is empty.");
        }

        if (!response.TryGetProperty(
                "result",
                out _))
        {
            return ResponseValidationResult.Failure(
                "action response result is missing.");
        }

        if (response.TryGetProperty(
                "meta",
                out var meta))
        {
            var metaValidation =
                ValidateMeta(meta);

            if (!metaValidation.IsValid)
            {
                return metaValidation;
            }
        }

        return ResponseValidationResult.Success();
    }

    private static ResponseValidationResult ValidateErrorResponse(
        JsonElement response)
    {
        var allowedFields =
            new HashSet<string>(
                new[]
                {
                    "status",
                    "code",
                    "message",
                    "retryable",
                    "details",
                    "meta"
                },
                StringComparer.Ordinal);

        foreach (var property in response.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                return ResponseValidationResult.Failure(
                    $"action error response contains " +
                    $"undeclared field '{property.Name}'.");
            }
        }

        if (!response.TryGetProperty(
                "code",
                out var code) ||
            code.ValueKind != JsonValueKind.String)
        {
            return ResponseValidationResult.Failure(
                "action error response code is missing.");
        }

        if (string.IsNullOrWhiteSpace(code.GetString()))
        {
            return ResponseValidationResult.Failure(
                "action error response code is empty.");
        }

        if (!response.TryGetProperty(
                "message",
                out var message) ||
            message.ValueKind != JsonValueKind.String)
        {
            return ResponseValidationResult.Failure(
                "action error response message is missing.");
        }

        if (string.IsNullOrWhiteSpace(message.GetString()))
        {
            return ResponseValidationResult.Failure(
                "action error response message is empty.");
        }

        if (!response.TryGetProperty(
                "retryable",
                out var retryable) ||
            (retryable.ValueKind != JsonValueKind.True &&
             retryable.ValueKind != JsonValueKind.False))
        {
            return ResponseValidationResult.Failure(
                "action error response retryable is missing.");
        }

        if (!response.TryGetProperty(
                "details",
                out _))
        {
            return ResponseValidationResult.Failure(
                "action error response details is missing.");
        }

        if (response.TryGetProperty(
                "meta",
                out var meta))
        {
            var metaValidation =
                ValidateMeta(meta);

            if (!metaValidation.IsValid)
            {
                return metaValidation;
            }
        }

        return ResponseValidationResult.DomainError(
            response);
    }

    private static ResponseValidationResult ValidateMeta(
        JsonElement meta)
    {
        if (meta.ValueKind != JsonValueKind.Object)
        {
            return ResponseValidationResult.Failure(
                "action response meta must be a JSON object.");
        }

        var allowedFields =
            new HashSet<string>(
                new[]
                {
                    "correlationId",
                    "actionVersion"
                },
                StringComparer.Ordinal);

        foreach (var property in meta.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                return ResponseValidationResult.Failure(
                    $"action response meta contains " +
                    $"undeclared field '{property.Name}'.");
            }
        }

        if (!meta.TryGetProperty(
                "correlationId",
                out var correlationId) ||
            correlationId.ValueKind != JsonValueKind.String)
        {
            return ResponseValidationResult.Failure(
                "action response meta.correlationId is missing.");
        }

        if (string.IsNullOrWhiteSpace(correlationId.GetString()))
        {
            return ResponseValidationResult.Failure(
                "action response meta.correlationId is empty.");
        }

        if (!meta.TryGetProperty(
                "actionVersion",
                out var actionVersion) ||
            actionVersion.ValueKind != JsonValueKind.Number ||
            !actionVersion.TryGetInt32(out _))
        {
            return ResponseValidationResult.Failure(
                "action response meta.actionVersion is missing.");
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
            CollectErrors(
                detail,
                errors);
        }
    }
}

public sealed record ResponseValidationResult(
    bool IsValid,
    bool IsDomainError,
    string? ErrorMessage,
    JsonElement? Response)
{
    public static ResponseValidationResult Success()
        => new(
            true,
            false,
            null,
            null);

    public static ResponseValidationResult Failure(
        string message)
        => new(
            false,
            false,
            message,
            null);

    public static ResponseValidationResult DomainError(
        JsonElement response)
        => new(
            false,
            true,
            null,
            response.Clone());
}