using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Test;

public class JwtConsentTokenFactoryTests
{
    private const string AgentSlug = "agent_acme";
    private const string BoardSlug = "mockboard_eu";

    [Fact]
    public void IssueToken_CreatesJwt_ES256_WithExpectedClaims_AndLedgerHash()
    {
        using var db = CreateDb();
        var tenant = SeedAgentTenant(db, AgentSlug);

        var options = Options.Create(new ConsentTokenOptions
        {
            Issuer = "https://issuer.test",
            TokenLifetimeDays = 180,
            KeyLifetimeDays = 365,
            RotationLeadDays = 30
        });
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), options, new TestLogger<JwtConsentTokenFactory>());

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "alice@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);

        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = AgentSlug,
            BoardTenantId = BoardSlug,
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMonths(6)
        };

        var issued = factory.IssueToken(consent, candidate);

        issued.Token.Should().NotBeNullOrEmpty();
        issued.KeyId.Should().NotBeNullOrEmpty();
        issued.Algorithm.Should().Be(SecurityAlgorithms.EcdsaSha256);

        consent.TokenId.Should().Be(issued.TokenId);
        consent.TokenKeyId.Should().Be(issued.KeyId);
        consent.TokenAlgorithm.Should().Be(SecurityAlgorithms.EcdsaSha256);
        consent.TokenHash.Should().Be(issued.TokenHash);

        db.SaveChanges();

        // Validate JWT using the public JWK stored for the tenant key
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        jwt.Header.Alg.Should().Be("ES256");
        jwt.Header.Kid.Should().Be(issued.KeyId);

        var key = db.TenantKeys.AsNoTracking().Single(k => k.TenantId == tenant.Id && k.KeyId == issued.KeyId);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = consent.BoardTenantId,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new JsonWebKey(key.PublicJwk)
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(issued.Token, validationParameters, out _);

        // Claims must match consent and candidate
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(candidate.Id.ToString());
        principal.FindFirst("cid")?.Value.Should().Be(consent.Id.ToString());
        principal.FindFirst("agent")?.Value.Should().Be(consent.AgentTenantId);
        principal.FindFirst("board")?.Value.Should().Be(consent.BoardTenantId);
        principal.FindFirst("scope")?.Value.Should().Be(consent.Scopes);
        principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value.Should().Be(issued.TokenId.ToString());

        // Ledger record exists and hash matches recomputation
        var record = db.ConsentTokens.AsNoTracking().Single(r => r.TokenId == issued.TokenId);
        record.KeyId.Should().Be(issued.KeyId);
        record.TokenHash.Should().Be(ComputeHash(issued.Token));
    }

    [Fact]
    public void IssueToken_RotatesKey_WhenWithinRotationLead()
    {
        using var db = CreateDb();
        SeedAgentTenant(db, AgentSlug);

        var options = Options.Create(new ConsentTokenOptions
        {
            Issuer = "https://issuer.test",
            TokenLifetimeDays = 180,
            KeyLifetimeDays = 30, // factory clamps minimum to 30
            RotationLeadDays = 30 // equal to lifetime → triggers rotation on subsequent issuance
        });
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), options, new TestLogger<JwtConsentTokenFactory>());

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "bob@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);
        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = AgentSlug,
            BoardTenantId = BoardSlug,
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        var t1 = factory.IssueToken(consent, candidate);
        db.SaveChanges();
        var firstKid = t1.KeyId;

        // Second issuance should rotate and retire the first key
        var t2 = factory.IssueToken(consent, candidate);
        var secondKid = t2.KeyId;

        firstKid.Should().NotBeNullOrEmpty();
        secondKid.Should().NotBe(firstKid);

        db.SaveChanges();
        var keys = db.TenantKeys.AsNoTracking().OrderBy(k => k.CreatedAt).ToArray();
        keys.Should().HaveCount(2);
        keys[0].KeyId.Should().Be(firstKid);
        keys[0].Status.Should().Be(TenantKeyStatus.Retired);
        keys[1].KeyId.Should().Be(secondKid);
        keys[1].Status.Should().Be(TenantKeyStatus.Active);
    }

    [Fact]
    public async Task RotateAsync_RetiresCurrentAndActivatesNew()
    {
        using var db = CreateDb();
        SeedAgentTenant(db, AgentSlug);

        var options = Options.Create(new ConsentTokenOptions
        {
            Issuer = "https://issuer.test",
            TokenLifetimeDays = 180,
            KeyLifetimeDays = 30,
            RotationLeadDays = 0
        });
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), options, new TestLogger<JwtConsentTokenFactory>());

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "carol@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);
        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = AgentSlug,
            BoardTenantId = BoardSlug,
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(10)
        };

        // Ensure a first active key exists
        var t1 = factory.IssueToken(consent, candidate);
        db.SaveChanges();

        var before = db.TenantKeys.AsNoTracking().Single(k => k.KeyId == t1.KeyId);
        before.Status.Should().Be(TenantKeyStatus.Active);

        var newKey = await factory.RotateAsync(AgentSlug);
        newKey.Status.Should().Be(TenantKeyStatus.Active);

        var after = db.TenantKeys.AsNoTracking().OrderBy(k => k.CreatedAt).ToArray();
        after.Should().HaveCount(2);
        after[0].Status.Should().Be(TenantKeyStatus.Retired);
        after[0].RetiredAt.Should().NotBeNull();
        after[1].KeyId.Should().Be(newKey.KeyId);
        after[1].Status.Should().Be(TenantKeyStatus.Active);
    }

    [Fact]
    public void IssueToken_Throws_WhenTenantNotFound()
    {
        using var db = CreateDb();
        var options = Options.Create(new ConsentTokenOptions());
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), options, new TestLogger<JwtConsentTokenFactory>());

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "nobody@example.com", CreatedAt = DateTime.UtcNow };
        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = "missing-agent",
            BoardTenantId = BoardSlug,
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        var act = () => factory.IssueToken(consent, candidate);
        act.Should().Throw<InvalidOperationException>();
    }

    private static GatewayDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GatewayDbContext(options);
    }

    private static Tenant SeedAgentTenant(GatewayDbContext db, string slug)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            DisplayName = slug,
            Type = TenantType.Agent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }

    private static string ComputeHash(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed class NoopDataProtectionProvider : IDataProtectionProvider, IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
