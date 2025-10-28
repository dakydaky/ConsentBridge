using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Gateway.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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

public sealed class JwtAccessTokenFactory : IAccessTokenFactory
{
    private readonly JwtAccessTokenOptions _options;
    private readonly JwtSecurityTokenHandler _handler;
    private readonly byte[] _signingKey;

    public JwtAccessTokenFactory(IOptions<JwtAccessTokenOptions> options)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("Auth:Jwt:SigningKey must be configured.");
        }
        _handler = new JwtSecurityTokenHandler();
        _signingKey = Encoding.UTF8.GetBytes(_options.SigningKey);
    }

    public AccessTokenResult IssueToken(Tenant tenant, TenantCredential credential, IReadOnlyList<string> scopes)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, tenant.Slug),
            new Claim("tenant_id", tenant.Id.ToString()),
            new Claim("tenant_type", tenant.Type.ToString()),
            new Claim("client_id", credential.ClientId),
            new Claim("scope", string.Join(' ', scopes))
        };

        var creds = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        return new AccessTokenResult(_handler.WriteToken(token), expires, scopes);
    }
}

public sealed class JwtAccessTokenOptions
{
    public string Issuer { get; set; } = "consent-apply-gateway";
    public string Audience { get; set; } = "consent-apply-gateway";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 30;
}
