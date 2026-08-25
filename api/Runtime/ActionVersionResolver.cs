using ModuleDev.Contracts;
using ModuleDev.Api.Database;

namespace ModuleDev.Api.Runtime;

public sealed class ActionVersionResolver
{
    private readonly IActionRepository _repository;

    public ActionVersionResolver(
        IActionRepository repository)
    {
        _repository = repository;
    }

    public Task<ActionManifest?> ResolveAsync(
        string module,
        string action,
        int? explicitVersion,
        CancellationToken cancellationToken)
    {
        return explicitVersion.HasValue
            ? _repository.GetExplicitAsync(
                module,
                action,
                explicitVersion.Value,
                cancellationToken)
            : _repository.GetDefaultAsync(
                module,
                action,
                cancellationToken);
    }
}