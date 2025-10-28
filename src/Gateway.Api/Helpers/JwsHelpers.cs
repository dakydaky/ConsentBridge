using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Api;

internal static class JwsHelpers
{
    private static readonly JsonSerializerOptions HeaderSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static bool TryParseDetachedJws(string jws, ReadOnlySpan<byte> canonicalJson, out JwsHeader header)
    {
        header = default!;
        if (string.IsNullOrWhiteSpace(jws))
        {
            return false;
        }

        var parts = jws.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var headerEncoded = parts[0];
        var payloadEncoded = parts[1];
        var expectedPayload = Base64UrlEncoder.Encode(canonicalJson.ToArray());
        if (!string.Equals(payloadEncoded, expectedPayload, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(headerEncoded));
            header = JsonSerializer.Deserialize<JwsHeader>(headerJson, HeaderSerializerOptions)
                     ?? throw new JsonException("Invalid JWS header.");
            return !string.IsNullOrWhiteSpace(header.Alg);
        }
        catch
        {
            return false;
        }
    }

    internal sealed record JwsHeader(string? Alg, string? Kid, string? Typ);
}

