using ModuleDev.Contracts;

namespace ModuleDev.Workflow;

public interface IActionCatalog
{
    Task<ActionManifest?> GetAsync(
        string module,
        string action,
        int version,
        CancellationToken cancellationToken = default);
}