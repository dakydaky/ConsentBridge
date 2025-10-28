using System.Text;
using System.Text.Json;
using Gateway.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Gateway.Infrastructure;

public sealed class JwksJwsVerifier : IJwsVerifier
{
    private const int KeySizeBytes = 32;
    private static readonly JsonSerializerOptions HeaderSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITenantKeyStore _keyStore;
    private readonly ILogger<JwksJwsVerifier>? _logger;

    public JwksJwsVerifier(ITenantKeyStore keyStore, ILogger<JwksJwsVerifier>? logger = null)
    {
        _keyStore = keyStore;
        _logger = logger;
    }

    public bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(jws) || string.IsNullOrWhiteSpace(tenantSlug))
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

        var expectedPayloadEncoded = Base64UrlEncoder.Encode(canonicalJson.ToArray());
        if (!string.Equals(payloadEncoded, expectedPayloadEncoded, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryDecodeHeader(headerEncoded, out var header))
        {
            return false;
        }

        if (!_keyStore.TryGetKeys(tenantSlug, out var jwks) || jwks?.Keys is null || jwks.Keys.Count == 0)
        {
            _logger?.LogWarning("No JWKS configured for tenant {Tenant}", tenantSlug);
            return false;
        }

        var candidateKeys = ResolveCandidateKeys(jwks.Keys, header.Kid, header.Alg!);
        if (candidateKeys.Count == 0)
        {
            _logger?.LogWarning("No matching JWK found for tenant {Tenant} and kid {Kid}", tenantSlug, header.Kid);
            return false;
        }

        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        var signatureBytes = Base64UrlEncoder.DecodeBytes(signatureEncoded);
        foreach (var key in candidateKeys)
        {
            if (VerifyWithKey(key, signingInput, signatureBytes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeHeader(string headerEncoded, out JwsHeader header)
    {
        header = default!;
        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(headerEncoded));
            header = JsonSerializer.Deserialize<JwsHeader>(headerJson, HeaderSerializerOptions)
                ?? throw new JsonException("Header null");
            return !string.IsNullOrWhiteSpace(header.Alg);
        }
        catch
        {
            return false;
        }
    }

    private static List<JsonWebKey> ResolveCandidateKeys(IList<JsonWebKey> keys, string? kid, string alg)
    {
        var list = new List<JsonWebKey>();
        foreach (var key in keys)
        {
            if (!string.Equals(key.Kty, JsonWebAlgorithmsKeyTypes.EllipticCurve, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(alg, SecurityAlgorithms.EcdsaSha256, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(alg, "ES256", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(key.Alg) &&
                !string.Equals(key.Alg, alg, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(kid) &&
                !string.Equals(key.Kid, kid, StringComparison.Ordinal))
            {
                continue;
            }

            list.Add(key);
        }

        return list;
    }

    private static bool VerifyWithKey(JsonWebKey key, string signingInput, byte[] signature)
    {
        if (signature.Length != KeySizeBytes * 2)
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = DecodeComponent(key.X),
                    Y = DecodeComponent(key.Y)
                }
            };
            ecdsa.ImportParameters(parameters);
            var data = Encoding.UTF8.GetBytes(signingInput);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DecodeComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Missing coordinate component in JWK.");
        }

        var bytes = Base64UrlEncoder.DecodeBytes(value);

        if (bytes.Length > KeySizeBytes)
        {
            var offset = 0;
            while (offset < bytes.Length - KeySizeBytes && bytes[offset] == 0x00)
            {
                offset++;
            }

            if (bytes.Length - offset > KeySizeBytes)
            {
                throw new InvalidOperationException("Invalid P-256 coordinate length.");
            }

            bytes = bytes[offset..];
        }

        if (bytes.Length < KeySizeBytes)
        {
            var padded = new byte[KeySizeBytes];
            Buffer.BlockCopy(bytes, 0, padded, KeySizeBytes - bytes.Length, bytes.Length);
            return padded;
        }

        return bytes;
    }

    private sealed record JwsHeader(string? Alg, string? Kid, string? Typ);
}
