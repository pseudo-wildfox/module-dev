using System.Text.Json;

namespace ModuleDev.Workflow;

public static class WorkflowMapLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowOutOfOrderMetadataProperties = true
    };

    public static WorkflowMap Load(string json)
    {
        using var document = JsonDocument.Parse(json);

        var map = document.RootElement.Deserialize<WorkflowMap>(Options);

        return map
            ?? throw new InvalidOperationException(
                "Workflow map JSON is empty.");
    }
}