namespace Gateway.Domain;

public interface IJwsVerifier
{
    bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid);
}

public sealed class AcceptAllVerifier : IJwsVerifier
{
    public bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid) => true; // demo only
}

public interface IClientSecretHasher
{
    string HashSecret(string secret);
    bool Verify(string secret, string hash);
}

public interface IConsentTokenFactory
{
    ConsentTokenIssueResult IssueToken(Consent consent, Candidate candidate);
}

public record ConsentTokenIssueResult(string Token, Guid TokenId, DateTime ExpiresAt);

public interface IAccessTokenFactory
{
    AccessTokenResult IssueToken(Tenant tenant, TenantCredential credential, IReadOnlyList<string> scopes);
}

public record AccessTokenResult(string Token, DateTime ExpiresAt, IReadOnlyList<string> Scopes);
