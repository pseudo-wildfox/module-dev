using System.Text.Json;

namespace ModuleDev.Contracts;

public static class ActionManifestLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static ActionManifest Load(string json)
    {
        using var document = JsonDocument.Parse(json);

        var manifest = document.RootElement.Deserialize<ActionManifest>(
            Options);

        return manifest
            ?? throw new InvalidOperationException(
                "Manifest JSON is empty.");
    }
}