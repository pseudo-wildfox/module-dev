using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

public sealed class ManualStep : FlowStep
{
    [JsonPropertyName("allowed_outcomes")]
    public required IReadOnlyList<string> AllowedOutcomes { get; init; }
}