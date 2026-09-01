using ModuleDev.Contracts;

namespace Workflow.Tests.Fakes;

public sealed class FakeActionCatalog : ModuleDev.Workflow.IActionCatalog
{
    private readonly Dictionary<(string Module, string Action, int Version), ActionManifest> _actions = new();

    public void Add(ActionManifest manifest)
    {
        _actions[
            (manifest.Module, manifest.Action, manifest.Version)
        ] = manifest;
    }

    public Task<ActionManifest?> GetAsync(
        string module,
        string action,
        int version,
        CancellationToken cancellationToken = default)
    {
        _actions.TryGetValue(
            (module, action, version),
            out var manifest);

        return Task.FromResult(manifest);
    }
}