using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class Transition
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }
}