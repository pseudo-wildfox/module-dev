using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class WaitSignalStep : FlowStep
{
    [JsonPropertyName("signal_type")]
    public required string SignalType { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }
}