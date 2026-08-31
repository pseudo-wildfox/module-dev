using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class TaskDefinition
{
    [JsonPropertyName("service")]
    public required string Service { get; init; }

    [JsonPropertyName("module")]
    public required string Module { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("action_version")]
    public required int ActionVersion { get; init; }

    [JsonPropertyName("required_policy")]
    public required IReadOnlyList<string> RequiredPolicy { get; init; }

    [JsonPropertyName("timeout_ms")]
    public required int TimeoutMs { get; init; }

    [JsonPropertyName("retry")]
    public required RetryPolicy Retry { get; init; }

    [JsonPropertyName("input_mapping")]
    public required IReadOnlyDictionary<string, string> InputMapping { get; init; }

    [JsonPropertyName("input_constants")]
    public required JsonElement InputConstants { get; init; }
}