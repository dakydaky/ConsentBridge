using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Gateway.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Test;

public class JwksJwsVerifierTests
{
    [Fact]
    public void RejectsOldSignatureAfterKeyRotation()
    {
        const string tenant = "agent_acme";
        var payloadObject = new { hello = "world" };
        var payloadJson = JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var oldKeySet = CreateJwks("kid-old", out var oldKey);
        var newKeySet = CreateJwks("kid-new", out var newKey);

        var store = new SwitchableKeyStore(tenant, oldKeySet);
        var verifier = new JwksJwsVerifier(store);

        var oldSignature = SignDetached(oldKey, payloadJson, "kid-old");
        verifier.VerifyDetached(Encoding.UTF8.GetBytes(payloadJson), oldSignature, tenant).Should().BeTrue();

        store.Update(newKeySet);

        verifier.VerifyDetached(Encoding.UTF8.GetBytes(payloadJson), oldSignature, tenant).Should().BeFalse();

        var newSignature = SignDetached(newKey, payloadJson, "kid-new");
        verifier.VerifyDetached(Encoding.UTF8.GetBytes(payloadJson), newSignature, tenant).Should().BeTrue();
    }

    [Fact]
    public void ReturnsFalseWhenTenantHasNoKeys()
    {
        var payloadJson = JsonSerializer.Serialize(new { foo = "bar" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var store = new SwitchableKeyStore("agent_acme", new JsonWebKeySet("{\"keys\":[]}"));
        var verifier = new JwksJwsVerifier(store);

        var result = verifier.VerifyDetached(Encoding.UTF8.GetBytes(payloadJson), "invalid.jws.signature", "agent_acme");
        result.Should().BeFalse();
    }

    private static JsonWebKeySet CreateJwks(string kid, out ECDsa key)
    {
        key = ECDsa.Create();
        key.KeySize = 256;
        var parameters = key.ExportParameters(true);

        var jwk = new
        {
            kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            use = JsonWebKeyUseNames.Sig,
            alg = SecurityAlgorithms.EcdsaSha256,
            kid,
            crv = "P-256",
            x = Base64UrlEncoder.Encode(parameters.Q.X),
            y = Base64UrlEncoder.Encode(parameters.Q.Y)
        };

        var json = JsonSerializer.Serialize(new { keys = new[] { jwk } });
        return new JsonWebKeySet(json);
    }

    private static string SignDetached(ECDsa key, string payloadJson, string kid)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "ES256", kid, typ = "JOSE" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var headerEncoded = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson));
        var payloadEncoded = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        var signature = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        var signatureEncoded = Base64UrlEncoder.Encode(signature);
        return $"{signingInput}.{signatureEncoded}";
    }

    private sealed class SwitchableKeyStore : ITenantKeyStore
    {
        private JsonWebKeySet _jwks;
        private readonly string _slug;

        public SwitchableKeyStore(string slug, JsonWebKeySet jwks)
        {
            _slug = slug;
            _jwks = jwks;
        }

        public void Update(JsonWebKeySet jwks) => _jwks = jwks;

        public bool TryGetKeys(string tenantSlug, out JsonWebKeySet? jwks)
        {
            if (string.Equals(tenantSlug, _slug, StringComparison.Ordinal))
            {
                jwks = _jwks;
                return true;
            }

            jwks = null;
            return false;
        }

        public IEnumerable<KeyValuePair<string, JsonWebKeySet>> GetAll() =>
            new[] { new KeyValuePair<string, JsonWebKeySet>(_slug, _jwks) };
    }
}
