using ModuleDev.Contracts;
using ModuleDev.Workflow;

namespace ModuleDev.Api.Database;

public sealed class ActionCatalogRepositoryAdapter : IActionCatalog
{
    private readonly IActionRepository _repository;

    public ActionCatalogRepositoryAdapter(
        IActionRepository repository)
    {
        _repository = repository;
    }

    public Task<ActionManifest?> GetAsync(
        string module,
        string action,
        int version,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetPublishedAsync(
            module,
            action,
            version,
            cancellationToken);
    }
}