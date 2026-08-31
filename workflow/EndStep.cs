using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class EndStep : FlowStep
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }
}