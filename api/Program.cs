using Npgsql;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModuleDev.Api.Database;
using ModuleDev.Api.Runtime;
using ModuleDev.Api.Security;
using ModuleDev.Contracts.Validation;
using ModuleDev.Contracts;
using ModuleDev.Api.Http;


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
builder.Services.AddScoped<ActionVersionResolver>();

builder.Services.AddSingleton<PolicyEvaluator>();
builder.Services.AddSingleton<JwtContextFactory>();

builder.Services.AddSingleton<RequestBodyReader>();
builder.Services.AddSingleton<ActionRequestValidator>();
builder.Services.AddSingleton<ActionResponseValidator>();
builder.Services.AddSingleton<IdempotencyStore>();

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

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "API is running");

app.MapGet(
    "/debug/validate-manifest",
    async (ActionManifestValidator validator) =>
    {
        var manifestJson = """
        {
          "contract_version": "course-1",
          "module": "test",
          "action": "echo",
          "version": 1,
          "http_method": "POST",
          "target_schema": "test",
          "target_function": "echo_v1",
          "request_schema": {
            "$schema": "https://json-schema.org/draft/2020-12/schema"
          },
          "response_schema": {
            "$schema": "https://json-schema.org/draft/2020-12/schema"
          },
          "outcomes": ["OK"],
          "required_policy": [],
          "idempotency_mode": "none",
          "idempotency_scope": "none",
          "timeout_ms": 5000,
          "enabled": false,
          "is_default": false
        }
        """;

        return validator.Validate(manifestJson);
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
                        message = "The same Idempotency-Key was used with a different payload.",
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