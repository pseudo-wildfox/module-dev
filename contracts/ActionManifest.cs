using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModuleDev.Contracts;

public sealed class ActionManifest
{
    [JsonPropertyName("contract_version")]
    public required string ContractVersion { get; init; }

    [JsonPropertyName("module")]
    public required string Module { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("http_method")]
    public required string HttpMethod { get; init; }

    [JsonPropertyName("target_schema")]
    public required string TargetSchema { get; init; }

    [JsonPropertyName("target_function")]
    public required string TargetFunction { get; init; }

    [JsonPropertyName("request_schema")]
    public required JsonElement RequestSchema { get; init; }

    [JsonPropertyName("response_schema")]
    public required JsonElement ResponseSchema { get; init; }

    [JsonPropertyName("outcomes")]
    public required IReadOnlyList<string> Outcomes { get; init; }

    [JsonPropertyName("required_policy")]
    public required IReadOnlyList<string> RequiredPolicy { get; init; }

    [JsonPropertyName("idempotency_mode")]
    public required string IdempotencyMode { get; init; }

    [JsonPropertyName("idempotency_scope")]
    public required string IdempotencyScope { get; init; }

    [JsonPropertyName("timeout_ms")]
    public required int TimeoutMs { get; init; }

    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    [JsonPropertyName("is_default")]
    public required bool IsDefault { get; init; }
}