using System.Text.Json;
using System.Text.Json.Nodes;
using ModuleDev.Api.Runtime;
using ModuleDev.Contracts;
using ModuleDev.ActionRuntime;

namespace Api.Tests.Contract;

public sealed class ActionResponseContractTests
{
    public ActionResponseContractTests() { }

    [Fact]
    public void Validate_ShouldRejectMalformedErrorEnvelope()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "error",
              "code": 123,
              "message": {},
              "retryable": "not-a-bool",
              "details": [],
              "garbage": "unexpected"
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    [Fact]
    public void Validate_ShouldRejectExtraSuccessEnvelopeFields()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "outcome": "OK",
              "result": {
                "echo": {},
                "principal": "test"
              },
              "garbage": "unexpected"
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ShouldRejectSuccessEnvelopeWithoutOutcome()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "result": {
                "echo": {},
                "principal": "test"
              }
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    [Fact]
    public void Validate_ShouldRejectResultThatDoesNotMatchResponseSchema()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "outcome": "OK",
              "result": {
                "echo": {},
                "principal": 123
              }
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    [Fact]
    public void Validate_ShouldAcceptValidSuccessEnvelope()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "outcome": "OK",
              "result": {
                "echo": {},
                "principal": "test"
              }
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.True(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    [Fact]
    public void Validate_ShouldAcceptValidErrorEnvelope()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "error",
              "code": "test.error",
              "message": "Something went wrong.",
              "retryable": false,
              "details": {}
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
        Assert.True(result.IsDomainError);
        Assert.NotNull(result.Response);
    }

    [Fact]
    public void Validate_ShouldRejectUndeclaredOutcome()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "outcome": "UNKNOWN",
              "result": {
                "echo": {},
                "principal": "test"
              }
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    [Fact]
    public void Validate_ShouldRejectSuccessEnvelopeWithoutResult()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "outcome": "OK"
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.False(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    [Fact]
    public void Validate_ShouldAcceptSuccessEnvelopeWithMeta()
    {
        var manifest = CreateManifest();

        using var document = JsonDocument.Parse(
            """
            {
              "status": "ok",
              "outcome": "OK",
              "result": {
                "echo": {},
                "principal": "test"
              },
              "meta": {
                "correlationId": "1a8e608f-d94e-4333-be79-d589eafd3080",
                "actionVersion": 1
              }
            }
            """
        );

        var validator = new ActionResponseValidator();

        var result = validator.Validate(manifest, document.RootElement);

        Assert.True(result.IsValid);
        Assert.False(result.IsDomainError);
    }

    private static ActionManifest CreateManifest()
    {
        var manifest = new JsonObject
        {
            ["contract_version"] = "course-1",
            ["module"] = "test",
            ["action"] = "echo",
            ["version"] = 0,
            ["http_method"] = "POST",
            ["target_schema"] = "test",
            ["target_function"] = "echo_v1",

            ["request_schema"] = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("value"),
                ["properties"] = new JsonObject
                {
                    ["value"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                        ["maxLength"] = 128,
                    },
                },
            },

            ["response_schema"] = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",

                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("echo", "principal"),

                ["properties"] = new JsonObject
                {
                    ["echo"] = new JsonObject { ["type"] = "object" },
                    ["principal"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                },
            },

            ["outcomes"] = new JsonArray("OK"),
            ["required_policy"] = new JsonArray(),
            ["idempotency_mode"] = "required",
            ["idempotency_scope"] = "principal_action",
            ["timeout_ms"] = 5000,
            ["enabled"] = true,
            ["is_default"] = false,
        };

        return manifest.Deserialize<ActionManifest>()!;
    }
}
