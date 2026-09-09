using ModuleDev.Api.Health;
using ModuleDev.Api.OpenApi;
using Npgsql;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModuleDev.Api.Database;
using ModuleDev.ActionRuntime;
using ModuleDev.Api.Runtime;
using ModuleDev.Api.Security;
using ModuleDev.Contracts.Validation;
using ModuleDev.Contracts;
using ModuleDev.Api.Http;
using ModuleDev.Workflow;


var builder = WebApplication.CreateBuilder(args);

var schemaPath = Path.Combine(
    AppContext.BaseDirectory,
    "contracts",
    "course-1",
    "action-manifest.schema.json");

var schemaJson = await File.ReadAllTextAsync(schemaPath);

builder.Services.AddSingleton(
    new ActionManifestValidator(schemaJson));


builder.Services.AddSingleton<DbConnectionFactory>();

builder.Services.AddScoped<IActionRepository, ActionRepository>();
builder.Services.AddScoped<IActionCatalog, ActionCatalogRepositoryAdapter>();

builder.Services.AddScoped<ActionVersionResolver>();

builder.Services.AddSingleton<PolicyEvaluator>();
builder.Services.AddSingleton<JwtContextFactory>();

builder.Services.AddSingleton<RequestBodyReader>();
builder.Services.AddSingleton<ActionRequestValidator>();
builder.Services.AddSingleton<ActionResponseValidator>();
builder.Services.AddSingleton<IdempotencyStore>();
builder.Services.AddSingleton<ManifestOpenApiBuilder>();

builder.Services.AddScoped<ActionExecutor>();

var issuer =
    GetRequiredEnvironmentVariable("COURSE_JWT_ISSUER");

var audience =
    GetRequiredEnvironmentVariable("COURSE_JWT_AUDIENCE");

var signingKey =
    GetRequiredEnvironmentVariable("COURSE_JWT_SIGNING_KEY");

if (Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException(
        "COURSE_JWT_SIGNING_KEY must be at least 32 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            ValidateAudience = true,
            ValidAudience = audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(signingKey)),

            ValidAlgorithms = new[]
            {
                SecurityAlgorithms.HmacSha256
            },

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var authorization =
                    context.HttpContext.Request.Headers.Authorization
                        .ToString();

                if (authorization.StartsWith(
                        "Bearer ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var token = authorization["Bearer ".Length..].Trim();

                    try
                    {
                        JwtRawPayloadValidator.Validate(token);

                        context.Token = token;
                    }
                    catch (InvalidJwtClaimException ex)
                    {
                        context.Fail(ex.Message);
                    }
                }

                return Task.CompletedTask;
            },

            OnChallenge = async context =>
            {
                context.HandleResponse();

                context.Response.StatusCode =
                    StatusCodes.Status401Unauthorized;

                context.Response.ContentType =
                    "application/json";

                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        status = "error",
                        code = "auth.invalid",
                        message = "JWT is invalid.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId =
                                context.HttpContext.TraceIdentifier,
                            actionVersion = (int?)null
                        }
                    });
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        "postgres",
        tags: new[] { "ready" });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "API is running");

app.MapGet(
    "/health/live",
    () => Results.Ok(new
    {
        status = "ok"
    }));

app.MapHealthChecks(
    "/health/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check =>
            check.Tags.Contains("ready")
    });


app.MapGet(
    "/openapi/actions/{module}/{action}/{version:int}.json",
    async (
        string module,
        string action,
        int version,
        IActionRepository actionRepository,
        ManifestOpenApiBuilder openApiBuilder,
        CancellationToken cancellationToken) =>
    {
        if (version < 1)
        {
            return Results.NotFound();
        }

        ActionManifest? manifest;

        try
        {
            manifest = await actionRepository.GetPublishedAsync(
                module,
                action,
                version,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "dependency.unavailable",
                    message = "PostgreSQL is unavailable.",
                    retryable = true,
                    details = new { }
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (manifest is null)
        {
            return Results.NotFound();
        }

        var openApiJson = openApiBuilder.Build(manifest);

        return Results.Text(
            openApiJson,
            "application/json",
            Encoding.UTF8,
            StatusCodes.Status200OK);
    });

app.MapGet(
    "/openapi/default.json",
    async (
        IActionRepository actionRepository,
        ManifestOpenApiBuilder openApiBuilder,
        CancellationToken cancellationToken) =>
    {
        IReadOnlyList<ActionManifest> manifests;

        try
        {
            manifests =
                await actionRepository.GetAllDefaultsAsync(
                    cancellationToken);
        }
        catch (NpgsqlException)
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "dependency.unavailable",
                    message = "PostgreSQL is unavailable.",
                    retryable = true,
                    details = new { }
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var openApiJson =
            openApiBuilder.BuildDefault(manifests);

        return Results.Text(
            openApiJson,
            "application/json",
            Encoding.UTF8,
            StatusCodes.Status200OK);
    });

app.MapPost(
    "/api/{module}/{action}",
    async (
        string module,
        string action,
        HttpContext httpContext,
        ActionVersionResolver versionResolver,
        PolicyEvaluator policyEvaluator,
        JwtContextFactory contextFactory,
        RequestBodyReader requestBodyReader,
        ActionRequestValidator requestValidator,
        ActionExecutor executor,
        CancellationToken cancellationToken) =>
    {
        var correlationId = Guid.NewGuid();

        var versionHeader =
            httpContext.Request.Headers["X-Action-Version"]
                .FirstOrDefault();

        int? explicitVersion = null;

        if (!string.IsNullOrWhiteSpace(versionHeader))
        {
            if (!int.TryParse(
                    versionHeader,
                    out var parsedVersion) ||
                parsedVersion < 1)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "request.invalid",
                        message = "X-Action-Version is invalid.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = (int?)null
                        }
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            explicitVersion = parsedVersion;
        }

        ActionManifest? manifest;

        try
        {
            manifest = await versionResolver.ResolveAsync(
                module,
                action,
                explicitVersion,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "dependency.unavailable",
                    message = "PostgreSQL is unavailable.",
                    retryable = true,
                    details = new { },
                    meta = new
                    {
                        correlationId,
                        actionVersion = explicitVersion
                    }
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (manifest is null)
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "action.not_found",
                    message = "Action is not available.",
                    retryable = false,
                    details = new { },
                    meta = new
                    {
                        correlationId,
                        actionVersion = explicitVersion
                    }
                },
                statusCode: StatusCodes.Status404NotFound);
        }

        TrustedPrincipal trustedPrincipal;

        try
        {
            trustedPrincipal =
                contextFactory.CreatePrincipal(
                    httpContext.User);
        }
        catch (InvalidJwtClaimException)
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "auth.invalid",
                    message = "JWT claims are invalid.",
                    retryable = false,
                    details = new { },
                    meta = new
                    {
                        correlationId,
                        actionVersion = manifest.Version
                    }
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!policyEvaluator.IsAllowed(
                manifest.RequiredPolicy,
                trustedPrincipal.Scopes))
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "access.denied",
                    message = "Access denied.",
                    retryable = false,
                    details = new { },
                    meta = new
                    {
                        correlationId,
                        actionVersion = manifest.Version
                    }
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        JsonDocument payload;

        try
        {
            payload = await requestBodyReader.ReadAsync(
                httpContext.Request,
                cancellationToken);
        }
        catch (InvalidJsonRequestException)
        {
            return Results.Json(
                new
                {
                    status = "error",
                    code = "request.invalid",
                    message = "request body contains invalid JSON.",
                    retryable = false,
                    details = new { },
                    meta = new
                    {
                        correlationId,
                        actionVersion = manifest.Version
                    }
                },
                statusCode: StatusCodes.Status400BadRequest);
        }

        using (payload)
        {
            if (!requestValidator.IsValid(
                    manifest.RequestSchema,
                    payload.RootElement))
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "payload.invalid",
                        message = "payload does not match schema.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var requestId =
                httpContext.Request.Headers["Idempotency-Key"]
                    .FirstOrDefault();

            if (manifest.IdempotencyMode == "required" &&
                string.IsNullOrWhiteSpace(requestId))
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "idempotency.required",
                        message = "Idempotency-Key header is required.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var deadline =
                DateTimeOffset.UtcNow.AddMilliseconds(
                    manifest.TimeoutMs);

            var context = new TrustedContext(
                trustedPrincipal.Principal,
                trustedPrincipal.Consumer,
                trustedPrincipal.Scopes,
                correlationId,
                requestId,
                deadline);

            string responseJson;

            try
            {
                responseJson = await executor.ExecuteAsync(
                    manifest,
                    context,
                    payload,
                    requestId,
                    cancellationToken);
            }
            catch (IdempotencyConflictException)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "idempotency.conflict",
                        message =
                            "The same Idempotency-Key was used with a different payload.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ActionDomainException ex)
            {
                var statusCode =
                    ex.Code switch
                    {
                        "operation.not_found"
                            => StatusCodes.Status404NotFound,

                        _ => StatusCodes.Status400BadRequest
                    };

                return Results.Json(
                    new
                    {
                        status = "error",
                        code = ex.Code,
                        message = ex.Message,
                        retryable = ex.Retryable,
                        details = ex.Details,
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: statusCode);
            }
            catch (ActionTimeoutException)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "action.timeout",
                        message = "Action execution timed out.",
                        retryable = true,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (ActionExecutionException)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "action.contract_violation",
                        message =
                            "action result violates the declared contract.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (NpgsqlException)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "dependency.unavailable",
                        message = "PostgreSQL is unavailable.",
                        retryable = true,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception)
            {
                return Results.Json(
                    new
                    {
                        status = "error",
                        code = "internal.error",
                        message = "An internal error occurred.",
                        retryable = false,
                        details = new { },
                        meta = new
                        {
                            correlationId,
                            actionVersion = manifest.Version
                        }
                    },
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Content(
                responseJson,
                "application/json",
                Encoding.UTF8,
                StatusCodes.Status200OK);
        }
    })
    .RequireAuthorization();

app.Run();

static string GetRequiredEnvironmentVariable(
    string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Environment variable '{name}' is required.");
    }

    return value;
}


public partial class Program
{
}