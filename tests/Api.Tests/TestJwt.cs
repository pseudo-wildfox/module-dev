using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

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

        var now =
            DateTimeOffset.UtcNow;

        var claims =
            new List<Claim>
            {
                new("sub", sub),
                new("consumer", consumer),
                new("scope", scope),
                new(
                    "iat",
                    now.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };


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

    public static string CreateCandidateWithoutIat()
    {
        var now = DateTimeOffset.UtcNow;

        return CreateRaw(
            header: new
            {
                alg = "HS256",
                typ = "JWT"
            },
            payload: new
            {
                iss = Issuer,
                aud = Audience,
                sub = "candidate-client",
                consumer = "web",
                scope = "payment:write payment:read workflow:read",
                nbf = now.ToUnixTimeSeconds(),
                exp = now.AddMinutes(5).ToUnixTimeSeconds()
            });
    }

    private static string CreateRaw(object header, object payload)
    {
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var encodedHeader = Base64UrlEncode(
            Encoding.UTF8.GetBytes(headerJson));

        var encodedPayload = Base64UrlEncode(
            Encoding.UTF8.GetBytes(payloadJson));

        var unsignedToken =
            $"{encodedHeader}.{encodedPayload}";

        var key = Encoding.UTF8.GetBytes(SigningKey);

        using var hmac = new HMACSHA256(key);

        var signature = hmac.ComputeHash(
            Encoding.UTF8.GetBytes(unsignedToken));

        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert
            .ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string CreateWorker()
    {
        return Create(
            "workflow-worker",
            "internal",
            "workflow:execute payment:internal");
    }


    public static string CreateCandidateWithNumericSub()
    {
        return CreateRaw(
            header: new
            {
                alg = "HS256",
                typ = "JWT"
            },
            payload: new
            {
                iss = Issuer,
                aud = Audience,
                sub = 12345,
                consumer = "web",
                scope = "payment:write payment:read workflow:read",
                nbf = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }

    public static string CreateCandidateWithNumericConsumer()
    {
        return CreateRaw(
            header: new
            {
                alg = "HS256",
                typ = "JWT"
            },
            payload: new
            {
                iss = Issuer,
                aud = Audience,
                sub = "candidate-client",
                consumer = 12345,
                scope = "payment:write payment:read workflow:read",
                nbf = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }

    public static string CreateCandidateWithBooleanScope()
    {
        return CreateRaw(
            header: new
            {
                alg = "HS256",
                typ = "JWT"
            },
            payload: new
            {
                iss = Issuer,
                aud = Audience,
                sub = "candidate-client",
                consumer = "web",
                scope = true,
                nbf = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }


    public static string CreateCandidateWithHs384()
    {
        return CreateRawWithAlgorithm(
            "HS384",
            new
            {
                iss = Issuer,
                aud = Audience,
                sub = "candidate-client",
                consumer = "web",
                scope = "payment:write payment:read workflow:read",
                nbf = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }

    private static string CreateRawWithAlgorithm(
        string algorithm,
        object payload)
    {
        var header = new
        {
            alg = algorithm,
            typ = "JWT"
        };

        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var encodedHeader = Base64UrlEncode(
            Encoding.UTF8.GetBytes(headerJson));

        var encodedPayload = Base64UrlEncode(
            Encoding.UTF8.GetBytes(payloadJson));

        var unsignedToken =
            $"{encodedHeader}.{encodedPayload}";

        using var hmac = new HMACSHA384(
            Encoding.UTF8.GetBytes(SigningKey));

        var signature = hmac.ComputeHash(
            Encoding.UTF8.GetBytes(unsignedToken));

        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }


    public static string CreateCandidateWithArrayScope()
    {
        return CreateRaw(
            header: new
            {
                alg = "HS256",
                typ = "JWT"
            },
            payload: new
            {
                iss = Issuer,
                aud = Audience,
                sub = "candidate-client",
                consumer = "web",
                scope = new[]
                {
                    "payment:write",
                    "payment:read",
                    "workflow:read"
                },
                nbf = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }

}