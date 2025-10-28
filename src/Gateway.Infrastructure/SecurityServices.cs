using System;
using System.Security.Cryptography;
using Gateway.Domain;

namespace Gateway.Infrastructure;

public sealed class DefaultClientSecretHasher : IClientSecretHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private const string Prefix = "cb-v1";

    public string HashSecret(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string secret, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var parts = hash.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var known = Convert.FromBase64String(parts[2]);
        var computed = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(known, computed);
    }
}

public sealed class DemoConsentTokenFactory : IConsentTokenFactory
{
    private readonly TimeSpan _lifetime;

    public DemoConsentTokenFactory(TimeSpan? lifetime = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromDays(180);
    }

    public ConsentTokenIssueResult IssueToken(Consent consent, Candidate candidate)
    {
        var tokenId = Guid.NewGuid();
        var expires = DateTime.UtcNow.Add(_lifetime);
        var token = $"ctok:{tokenId}";
        return new ConsentTokenIssueResult(token, tokenId, expires);
    }
}
