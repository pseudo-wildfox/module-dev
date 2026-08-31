using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class RetryPolicy
{
    [JsonPropertyName("max_attempts")]
    public required int MaxAttempts { get; init; }

    [JsonPropertyName("delays_ms")]
    public required IReadOnlyList<int> DelaysMs { get; init; }
}