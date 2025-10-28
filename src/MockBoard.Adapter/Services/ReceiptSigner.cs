using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MockBoard.Adapter.Services;

public sealed class ReceiptSigner
{
    private readonly MockBoardOptions _options;
    private static readonly JsonSerializerOptions HeaderOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public ReceiptSigner(IOptions<MockBoardOptions> options)
    {
        _options = options.Value;
    }

    public string Sign(BoardReceiptPayload payload, out byte[] canonicalPayload)
    {
        canonicalPayload = ReceiptJson.SerializePayload(payload);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(
            new HeaderModel("HS256", _options.BoardId, "JWT"),
            HeaderOptions);

        var headerEncoded = Base64UrlEncoder.Encode(headerBytes);
        var payloadEncoded = Base64UrlEncoder.Encode(canonicalPayload);
        var signingInput = $"{headerEncoded}.{payloadEncoded}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningSecret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        var signatureEncoded = Base64UrlEncoder.Encode(signatureBytes);

        return $"{signingInput}.{signatureEncoded}";
    }

    private sealed record HeaderModel(string Alg, string Kid, string Typ);
}
