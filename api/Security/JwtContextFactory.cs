using System.Security.Claims;

namespace ModuleDev.Api.Security;

public sealed class JwtContextFactory
{
    public TrustedPrincipal CreatePrincipal(
        ClaimsPrincipal principal)
    {
        var sub = GetRequiredClaim(principal, "sub");
        var consumer = GetRequiredClaim(principal, "consumer");
        var scope = GetRequiredClaim(principal, "scope");

        var scopes = scope
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new TrustedPrincipal(
            sub,
            consumer,
            scopes);
    }

    private static string GetRequiredClaim(
        ClaimsPrincipal principal,
        string name)
    {
        var claim = principal.FindFirst(name);

        if (claim is null ||
            string.IsNullOrWhiteSpace(claim.Value))
        {
            throw new InvalidJwtClaimException(
                $"Required claim '{name}' is missing.");
        }

        return claim.Value;
    }
}

public sealed record TrustedPrincipal(
    string Principal,
    string Consumer,
    IReadOnlyList<string> Scopes);

public sealed class InvalidJwtClaimException : Exception
{
    public InvalidJwtClaimException(string message)
        : base(message)
    {
    }
}