using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Api.Tests;

internal static class TestJwt
{
    private const string Issuer =
        "moduledev-course-test";

    private const string Audience =
        "moduledev-api-test";

    private const string SigningKey =
        "this-is-a-test-signing-key-at-least-32-bytes-long";

    public static string CreateCandidate()
    {
        return Create(
            "candidate-client",
            "web",
            "payment:write payment:read workflow:read");
    }

    public static string CreateDenied()
    {
        return Create(
            "denied-client",
            "test",
            "");
    }

    public static string CreateReader()
    {
        return Create(
            "candidate-client",
            "web",
            "payment:read");
    }

    private static string Create(
        string sub,
        string consumer,
        string scope)
    {
        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(SigningKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var claims =
            new List<Claim>
            {
                new("sub", sub),
                new("consumer", consumer),
                new("scope", scope)
            };

        var now =
            DateTimeOffset.UtcNow;

        var token =
            new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: now.AddMinutes(5).UtcDateTime,
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    public static string CreateAnotherCandidate()
    {
        return Create(
            "another-client",
            "web",
            "payment:write payment:read workflow:read");
    }
}