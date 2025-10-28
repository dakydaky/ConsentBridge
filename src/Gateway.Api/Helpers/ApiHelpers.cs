using Gateway.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Api;

internal static class ApiHelpers
{
    internal static IEnumerable<object> ProjectKeys(JsonWebKeySet jwks, string tenant)
    {
        if (jwks?.Keys is null)
        {
            yield break;
        }

        foreach (var key in jwks.Keys)
        {
            yield return new
            {
                tenant,
                kty = key.Kty,
                use = key.Use,
                alg = string.IsNullOrWhiteSpace(key.Alg) ? "ES256" : key.Alg,
                kid = key.Kid,
                crv = key.Crv,
                x = key.X,
                y = key.Y
            };
        }
    }

    internal static ApplicationRecordDto MapApplication(Gateway.Domain.Application application) =>
        new(
            application.Id,
            application.ConsentId,
            application.AgentTenantId,
            application.BoardTenantId,
            application.Status,
            application.SubmittedAt,
            application.PayloadHash,
            application.SubmissionSignature,
            application.SubmissionKeyId,
            application.SubmissionAlgorithm,
            application.Receipt,
            application.ReceiptSignature,
            application.ReceiptHash);

    internal static ConsentViewDto MapConsent(Consent consent) =>
        new(
            consent.Id,
            consent.AgentTenantId,
            consent.BoardTenantId,
            consent.ApprovedByEmail,
            SplitScopes(consent.Scopes),
            consent.Status,
            consent.IssuedAt,
            consent.ExpiresAt,
            consent.TokenIssuedAt,
            consent.TokenExpiresAt,
            consent.TokenId,
            consent.TokenKeyId,
            consent.TokenAlgorithm,
            consent.RevokedAt);

    internal static IReadOnlyList<string> SplitScopes(string scopes) =>
        string.IsNullOrWhiteSpace(scopes)
            ? Array.Empty<string>()
            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string GenerateVerificationCode()
    {
        var value = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }
}
