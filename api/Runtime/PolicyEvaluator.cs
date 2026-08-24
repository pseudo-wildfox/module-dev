namespace ModuleDev.Api.Runtime;

public sealed class PolicyEvaluator
{
    public bool IsAllowed(
        IReadOnlyCollection<string> requiredPolicies,
        IReadOnlyCollection<string> grantedScopes)
    {
        if (requiredPolicies.Count == 0)
        {
            return true;
        }

        var scopes = grantedScopes.ToHashSet(
            StringComparer.Ordinal);

        return requiredPolicies.All(scopes.Contains);
    }
}