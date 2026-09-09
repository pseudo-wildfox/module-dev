using System.Security.Claims;

namespace ModuleDev.Api.Security;

public sealed class JwtContextFactory
{
    public TrustedPrincipal CreatePrincipal(
        ClaimsPrincipal principal)
    {
        var sub = GetRequiredStringClaim(principal, "sub");
        var consumer = GetRequiredStringClaim(principal, "consumer");
        var scope = GetOptionalStringClaim(principal, "scope");

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

    private static string GetRequiredStringClaim(
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

        if (claim.ValueType != ClaimValueTypes.String)
        {
            throw new InvalidJwtClaimException(
                $"Claim '{name}' must be a string.");
        }

        return claim.Value;
    }

    private static string GetOptionalStringClaim(
        ClaimsPrincipal principal,
        string name)
    {
        var claims = principal
            .FindAll(name)
            .ToArray();

        if (claims.Length == 0)
        {
            return string.Empty;
        }

        if (claims.Length > 1)
        {
            throw new InvalidJwtClaimException(
                $"Claim '{name}' must occur at most once.");
        }

        var claim = claims[0];

        if (claim.ValueType != ClaimValueTypes.String)
        {
            throw new InvalidJwtClaimException(
                $"Claim '{name}' must be a string.");
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