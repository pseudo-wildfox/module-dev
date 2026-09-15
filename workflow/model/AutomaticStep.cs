using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class AutomaticStep : FlowStep
{
    [JsonPropertyName("task")]
    public required TaskDefinition Task { get; init; }
}