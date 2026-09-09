using System.Text;
using System.Text.Json;

namespace ModuleDev.Api.Security;

public static class JwtRawPayloadValidator
{
    public static void Validate(string token)
    {
        var parts = token.Split('.');

        if (parts.Length != 3)
        {
            throw new InvalidJwtClaimException("JWT must contain exactly three parts.");
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);

            using var payload = JsonDocument.Parse(payloadBytes);

            var root = payload.RootElement;

            RequireString(root, "sub");
            RequireString(root, "consumer");

            if (
                root.TryGetProperty("scope", out var scope)
                && scope.ValueKind != JsonValueKind.String
            )
            {
                throw new InvalidJwtClaimException("Claim 'scope' must be a string.");
            }

            if (!root.TryGetProperty("iat", out var iat))
            {
                throw new InvalidJwtClaimException("Required claim 'iat' is missing.");
            }

            if (iat.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidJwtClaimException("Claim 'iat' must be a number.");
            }
        }
        catch (InvalidJwtClaimException)
        {
            throw;
        }
        catch (FormatException)
        {
            throw new InvalidJwtClaimException("JWT payload is not valid base64url.");
        }
        catch (JsonException)
        {
            throw new InvalidJwtClaimException("JWT payload is not valid JSON.");
        }
    }

    private static void RequireString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw new InvalidJwtClaimException($"Required claim '{name}' is missing.");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidJwtClaimException($"Claim '{name}' must be a string.");
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');

        base64 += new string('=', (4 - base64.Length % 4) % 4);

        return Convert.FromBase64String(base64);
    }
}
