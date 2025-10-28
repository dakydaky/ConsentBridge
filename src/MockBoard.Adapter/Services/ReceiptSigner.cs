using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MockBoard.Adapter.Services;

public sealed class ReceiptSigner
{
    private const int KeySizeBytes = 32;
    private static readonly JsonSerializerOptions HeaderOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly ECParameters _parameters;
    private readonly string _keyId;
    private readonly MockBoardOptions _options;

    public ReceiptSigner(IOptions<MockBoardOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        var path = ResolvePath(environment.ContentRootPath, _options.PrivateJwkPath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"MockBoard private JWK not found at '{path}'.");
        }

        var json = File.ReadAllText(path);
        var jwk = JsonSerializer.Deserialize<EcPrivateJwk>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to parse MockBoard private JWK.");

        if (string.IsNullOrWhiteSpace(jwk.X) || string.IsNullOrWhiteSpace(jwk.Y) || string.IsNullOrWhiteSpace(jwk.D))
        {
            throw new InvalidOperationException("Private JWK missing curve parameters.");
        }

        _parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = DecodeComponent(jwk.X),
                Y = DecodeComponent(jwk.Y)
            },
            D = DecodeComponent(jwk.D)
        };
        _keyId = string.IsNullOrWhiteSpace(jwk.Kid) ? _options.BoardId : jwk.Kid!;
    }

    public string Sign(BoardReceiptPayload payload, out byte[] canonicalPayload)
    {
        canonicalPayload = ReceiptJson.SerializePayload(payload);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(
            new HeaderModel("ES256", _keyId, "JOSE"),
            HeaderOptions);

        var headerEncoded = Base64UrlEncoder.Encode(headerBytes);
        var payloadEncoded = Base64UrlEncoder.Encode(canonicalPayload);
        var signingInput = $"{headerEncoded}.{payloadEncoded}";

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(_parameters);
        var signatureBytes = ecdsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        if (signatureBytes.Length != KeySizeBytes * 2)
        {
            throw new InvalidOperationException("Unexpected ECDSA signature length.");
        }
        var signatureEncoded = Base64UrlEncoder.Encode(signatureBytes);

        return $"{signingInput}.{signatureEncoded}";
    }

    private static byte[] DecodeComponent(string value)
    {
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
                throw new InvalidOperationException("Invalid P-256 component length.");
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

    private static string ResolvePath(string contentRoot, string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRoot, configuredPath);

    private sealed record EcPrivateJwk(string? Kid, string? X, string? Y, string? D);
    private sealed record HeaderModel(string Alg, string Kid, string Typ);
}
