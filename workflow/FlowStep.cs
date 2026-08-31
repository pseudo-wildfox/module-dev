using System.Text.Json.Serialization;

namespace ModuleDev.Workflow;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AutomaticStep), "automatic")]
[JsonDerivedType(typeof(WaitSignalStep), "wait_signal")]
[JsonDerivedType(typeof(ManualStep), "manual")]
[JsonDerivedType(typeof(EndStep), "end")]
public abstract class FlowStep
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }
}