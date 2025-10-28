using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Infrastructure;

public sealed class Hs256JwsVerifier : IJwsVerifier
{
    private readonly ITenantSigningSecretProvider _secretProvider;
    private static readonly JsonSerializerOptions HeaderSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Hs256JwsVerifier(ITenantSigningSecretProvider secretProvider)
    {
        _secretProvider = secretProvider;
    }

    public bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid)
    {
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
        var signatureEncoded = parts[2];

        if (!TryDecodeHeader(headerEncoded, out var kid, out var alg))
        {
            return false;
        }

        if (!string.Equals(alg, SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(alg, "HS256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedKid) && !string.Equals(kid ?? expectedKid, expectedKid, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_secretProvider.TryGetSigningSecret(expectedKid, out var secret) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var expectedPayload = Base64UrlEncoder.Encode(canonicalJson.ToArray());
        if (!string.Equals(payloadEncoded, expectedPayload, StringComparison.Ordinal))
        {
            return false;
        }

        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        var computedSignature = Base64UrlEncoder.Encode(signatureBytes);

        return string.Equals(signatureEncoded, computedSignature, StringComparison.Ordinal);
    }

    private static bool TryDecodeHeader(string headerEncoded, out string? kid, out string? alg)
    {
        kid = null;
        alg = null;

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(headerEncoded));
            var header = JsonSerializer.Deserialize<HeaderModel>(headerJson, HeaderSerializerOptions);
            if (header is null)
            {
                return false;
            }

            kid = header.Kid;
            alg = header.Alg;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record HeaderModel(string? Alg, string? Kid, string? Typ);
}
