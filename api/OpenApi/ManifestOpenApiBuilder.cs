using System.Text.Json;
using ModuleDev.Contracts;

namespace ModuleDev.Api.OpenApi;

public sealed class ManifestOpenApiBuilder
{
    public string Build(ActionManifest manifest)
    {
        var path = $"/api/{manifest.Module}/{manifest.Action}";

        var parameters = new List<object>
        {
            new
            {
                name = "X-Action-Version",
                @in = "header",
                required = false,
                schema = new
                {
                    type = "integer",
                    minimum = 1,
                    @default = manifest.Version
                },
                description =
                    $"Explicit action version. This document describes version {manifest.Version}."
            }
        };

        if (!string.Equals(
                manifest.IdempotencyMode,
                "none",
                StringComparison.Ordinal))
        {
            parameters.Add(
                new
                {
                    name = "Idempotency-Key",
                    @in = "header",
                    required =
                        string.Equals(
                            manifest.IdempotencyMode,
                            "required",
                            StringComparison.Ordinal),
                    schema = new
                    {
                        type = "string",
                        minLength = 1
                    },
                    description =
                        "Idempotency key for this action."
                });
        }

        var successSchema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "status",
                "outcome",
                "result",
                "meta"
            },
            properties = new
            {
                status = new
                {
                    type = "string",
                    @const = "ok"
                },
                outcome = new
                {
                    type = "string",
                    @enum = manifest.Outcomes.ToArray()
                },
                result = manifest.ResponseSchema,
                meta = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[]
                    {
                        "correlationId",
                        "actionVersion"
                    },
                    properties = new
                    {
                        correlationId = new
                        {
                            type = "string",
                            format = "uuid"
                        },
                        actionVersion = new
                        {
                            type = "integer",
                            minimum = 1
                        }
                    }
                }
            }
        };

        var errorSchema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "status",
                "code",
                "message",
                "retryable",
                "details",
                "meta"
            },
            properties = new
            {
                status = new
                {
                    type = "string",
                    @const = "error"
                },
                code = new
                {
                    type = "string"
                },
                message = new
                {
                    type = "string"
                },
                retryable = new
                {
                    type = "boolean"
                },
                details = new
                {
                    type = "object"
                },
                meta = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[]
                    {
                        "correlationId",
                        "actionVersion"
                    },
                    properties = new
                    {
                        correlationId = new
                        {
                            type = "string",
                            format = "uuid"
                        },
                        actionVersion = new
                        {
                            type = "integer",
                            minimum = 1,
                            nullable = true
                        }
                    }
                }
            }
        };

        var document = new
        {
            openapi = "3.1.0",

            info = new
            {
                title = $"{manifest.Module}.{manifest.Action} v{manifest.Version}",
                version = $"{manifest.Version}",
                description =
                    "Machine-generated OpenAPI document from the published action manifest."
            },

            paths = new Dictionary<string, object>
            {
                [path] = new
                {
                    post = new
                    {
                        operationId =
                            $"{manifest.Module}_{manifest.Action}_v{manifest.Version}",

                        summary =
                            $"{manifest.Module}.{manifest.Action} version {manifest.Version}",

                        parameters,

                        security = new object[]
                        {
                            new Dictionary<string, string[]>
                            {
                                ["bearerAuth"] = Array.Empty<string>()
                            }
                        },

                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = manifest.RequestSchema
                                }
                            }
                        },

                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new
                            {
                                description = "Action executed successfully.",
                                content = new Dictionary<string, object>
                                {
                                    ["application/json"] = new
                                    {
                                        schema = successSchema
                                    }
                                }
                            },

                            ["400"] = ErrorResponse(
                                "Invalid request.",
                                errorSchema),

                            ["401"] = ErrorResponse(
                                "Authentication failed.",
                                errorSchema),

                            ["403"] = ErrorResponse(
                                "Access denied.",
                                errorSchema),

                            ["404"] = ErrorResponse(
                                "Action is not available.",
                                errorSchema),

                            ["409"] = ErrorResponse(
                                "Idempotency conflict.",
                                errorSchema),

                            ["422"] = ErrorResponse(
                                "Payload does not match the declared request schema.",
                                errorSchema),

                            ["500"] = ErrorResponse(
                                "Action contract violation or internal error.",
                                errorSchema),

                            ["503"] = ErrorResponse(
                                "Dependency is unavailable.",
                                errorSchema),

                            ["504"] = ErrorResponse(
                                "Action execution timed out.",
                                errorSchema)
                        }
                    }
                }
            },

            components = new
            {
                securitySchemes = new
                {
                    bearerAuth = new
                    {
                        type = "http",
                        scheme = "bearer",
                        bearerFormat = "JWT"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
    }

    private static object ErrorResponse(
        string description,
        object schema)
    {
        return new
        {
            description,
            content = new Dictionary<string, object>
            {
                ["application/json"] = new
                {
                    schema
                }
            }
        };
    }
}