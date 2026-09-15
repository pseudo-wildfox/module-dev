using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class WorkflowMap
{
    [JsonPropertyName("contract_version")]
    public required string ContractVersion { get; init; }

    [JsonPropertyName("flow_name")]
    public required string FlowName { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("start_step")]
    public required string StartStep { get; init; }

    [JsonPropertyName("steps")]
    public required IReadOnlyList<FlowStep> Steps { get; init; }

    [JsonPropertyName("transitions")]
    public required IReadOnlyList<Transition> Transitions { get; init; }
}