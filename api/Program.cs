using ModuleDev.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

var schemaPath = Path.Combine(
    AppContext.BaseDirectory,
    "contracts",
    "course-1",
    "action-manifest.schema.json");

var schemaJson = await File.ReadAllTextAsync(schemaPath);

builder.Services.AddSingleton(
    new ActionManifestValidator(schemaJson));

var app = builder.Build();

app.MapGet("/", () => "API is running");

app.MapGet("/debug/validate-manifest", async (
    ActionManifestValidator validator) =>
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

app.Run();