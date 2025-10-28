using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gateway.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

public sealed class JwtConsentTokenFactory : IConsentTokenFactory, IConsentKeyRotator
{
    private const string ProtectorPurpose = "ConsentBridge.TenantKeys";
    private const string DefaultAlgorithm = SecurityAlgorithms.EcdsaSha256;

    private readonly GatewayDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ConsentTokenOptions _options;
    private readonly ILogger<JwtConsentTokenFactory> _logger;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly TimeSpan _tokenLifetime;
    private readonly TimeSpan _keyLifetime;
    private readonly TimeSpan _rotationLeadTime;

    public JwtConsentTokenFactory(
        GatewayDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ConsentTokenOptions> options,
        ILogger<JwtConsentTokenFactory> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _tokenLifetime = TimeSpan.FromDays(Math.Max(1, _options.TokenLifetimeDays));
        _keyLifetime = TimeSpan.FromDays(Math.Max(30, _options.KeyLifetimeDays));
        _rotationLeadTime = TimeSpan.FromDays(Math.Clamp(_options.RotationLeadDays, 0, _options.KeyLifetimeDays));
    }

    public ConsentTokenIssueResult IssueToken(Consent consent, Candidate candidate)
    {
        if (consent is null) throw new ArgumentNullException(nameof(consent));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var now = DateTime.UtcNow;
        var tenant = _db.Tenants.FirstOrDefault(t => t.Slug == consent.AgentTenantId)
            ?? throw new InvalidOperationException($"Tenant {consent.AgentTenantId} not found when issuing consent token.");

        var signingKey = EnsureActiveKey(tenant, now);
        using var ecdsa = CreateEcdsa(signingKey);
        signingKey.LastUsedAt = now;

        var jti = Guid.NewGuid();
        var expires = now.Add(_tokenLifetime);
        var claims = BuildClaims(consent, candidate, jti, now);

        var credentials = new SigningCredentials(
            new ECDsaSecurityKey(ecdsa)
            {
                KeyId = signingKey.KeyId
            },
            DefaultAlgorithm);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: consent.BoardTenantId,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        var tokenString = _handler.WriteToken(token);
        var tokenHash = ComputeHash(tokenString);

        var record = new ConsentTokenRecord
        {
            Id = Guid.NewGuid(),
            ConsentId = consent.Id,
            TokenId = jti,
            TokenHash = tokenHash,
            KeyId = signingKey.KeyId,
            Algorithm = signingKey.Algorithm,
            IssuedAt = now,
            ExpiresAt = expires
        };
        _db.ConsentTokens.Add(record);

        consent.TokenId = jti;
        consent.TokenIssuedAt = now;
        consent.TokenExpiresAt = expires;
        consent.TokenKeyId = signingKey.KeyId;
        consent.TokenAlgorithm = signingKey.Algorithm;
        consent.TokenHash = tokenHash;

        _logger.LogInformation("Issued consent token {TokenId} for consent {ConsentId} using key {KeyId}", jti, consent.Id, signingKey.KeyId);

        return new ConsentTokenIssueResult(tokenString, jti, now, expires, signingKey.KeyId, signingKey.Algorithm, tokenHash);
    }

    public async Task<TenantKey> RotateAsync(string tenantSlug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            throw new ArgumentException("Tenant slug must be provided.", nameof(tenantSlug));
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantSlug, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantSlug} not found.");

        var now = DateTime.UtcNow;
        var current = await _db.TenantKeys
            .Where(k => k.TenantId == tenant.Id && k.Purpose == TenantKeyPurpose.ConsentToken && k.Status == TenantKeyStatus.Active)
            .OrderByDescending(k => k.ActivatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var newKey = RotateKey(tenant, current, now);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Debug rotation triggered for tenant {TenantSlug}. New key {KeyId} active until {Expires}.", tenantSlug, newKey.KeyId, newKey.ExpiresAt);
        return newKey;
    }

    private TenantKey EnsureActiveKey(Tenant tenant, DateTime now)
    {
        var key = _db.TenantKeys
            .Where(k => k.TenantId == tenant.Id && k.Purpose == TenantKeyPurpose.ConsentToken && k.Status == TenantKeyStatus.Active)
            .OrderByDescending(k => k.ActivatedAt)
            .FirstOrDefault();

        if (key is not null && key.ExpiresAt > now && key.ExpiresAt - now > _rotationLeadTime)
        {
            return key;
        }

        return RotateKey(tenant, key, now);
    }

    private TenantKey RotateKey(Tenant tenant, TenantKey? current, DateTime now)
    {
        if (current is not null && current.Status == TenantKeyStatus.Active)
        {
            current.Status = TenantKeyStatus.Retired;
            current.RetiredAt = now;
        }

        var keyId = $"ctok-{Guid.NewGuid():N}";
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var publicParams = ecdsa.ExportParameters(false);

        var jwk = new Dictionary<string, string>
        {
            ["kty"] = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            ["crv"] = JsonWebKeyECTypes.P256,
            ["use"] = JsonWebKeyUseNames.Sig,
            ["alg"] = DefaultAlgorithm,
            ["kid"] = keyId,
            ["x"] = Base64UrlEncoder.Encode(publicParams.Q.X),
            ["y"] = Base64UrlEncoder.Encode(publicParams.Q.Y)
        };

        var publicJwk = JsonSerializer.Serialize(jwk);
        var protectedPrivate = _protector.Protect(privateKey);

        var newKey = new TenantKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            KeyId = keyId,
            Purpose = TenantKeyPurpose.ConsentToken,
            Status = TenantKeyStatus.Active,
            Algorithm = DefaultAlgorithm,
            PublicJwk = publicJwk,
            PrivateKeyProtected = protectedPrivate,
            CreatedAt = now,
            ActivatedAt = now,
            ExpiresAt = now.Add(_keyLifetime)
        };

        _db.TenantKeys.Add(newKey);
        _logger.LogInformation("Generated new consent signing key {KeyId} for tenant {Tenant}", keyId, tenant.Slug);
        return newKey;
    }

    private ECDsa CreateEcdsa(TenantKey key)
    {
        var raw = _protector.Unprotect(key.PrivateKeyProtected);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(raw, out _);
        return ecdsa;
    }

    private static IEnumerable<Claim> BuildClaims(Consent consent, Candidate candidate, Guid jti, DateTime issuedAt)
    {
        yield return new Claim(JwtRegisteredClaimNames.Sub, candidate.Id.ToString());
        yield return new Claim("cid", consent.Id.ToString());
        yield return new Claim("agent", consent.AgentTenantId);
        yield return new Claim("board", consent.BoardTenantId);
        yield return new Claim("scope", consent.Scopes);
        yield return new Claim("ver", "1");
        yield return new Claim(JwtRegisteredClaimNames.Jti, jti.ToString());
        yield return new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(issuedAt).ToString(), ClaimValueTypes.Integer64);
    }

    private static string ComputeHash(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
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
        if (_signingKey.Length < 32)
        {
            throw new InvalidOperationException("Auth:Jwt:SigningKey must be at least 256 bits.");
        }
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

public sealed class ConsentTokenOptions
{
    public string Issuer { get; set; } = "https://consentbridge.local";
    public int TokenLifetimeDays { get; set; } = 180;
    public int KeyLifetimeDays { get; set; } = 365;
    public int RotationLeadDays { get; set; } = 30;
}
