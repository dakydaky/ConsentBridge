using FluentAssertions;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gateway.Test;

public class ConsentLifecycleServiceTests
{
    [Fact]
    public async Task Renew_AllowsWithinWindow_IssuesNewToken()
    {
        using var db = CreateDb();
        var tenant = SeedAgentTenant(db, "agent_acme");
        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "dave@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);
        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = tenant.Slug,
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddMonths(-5),
            ExpiresAt = DateTime.UtcNow.AddMonths(1),
            TokenIssuedAt = DateTime.UtcNow.AddDays(-20),
            TokenExpiresAt = DateTime.UtcNow.AddDays(5)
        };
        db.Consents.Add(consent);
        db.SaveChanges();

        var tokenOptions = Options.Create(new ConsentTokenOptions());
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), tokenOptions, new TestLogger<JwtConsentTokenFactory>());
        var lifecycleOptions = Options.Create(new ConsentLifecycleOptions { RenewalLeadDays = 7, ExpiryGraceDays = 3 });
        var service = new ConsentLifecycleService(db, factory, lifecycleOptions, new NoopAuditSink());

        var result = await service.RenewAsync(consent.Id);
        result.Should().NotBeNull();
        var updated = db.Consents.Single(c => c.Id == consent.Id);
        updated.TokenId.Should().Be(result!.TokenId);
        updated.TokenExpiresAt.Should().Be(result!.ExpiresAt);
    }

    [Fact]
    public async Task Renew_DeniesAfterGrace_ReturnsNull()
    {
        using var db = CreateDb();
        var tenant = SeedAgentTenant(db, "agent_acme");
        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "erin@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);
        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = tenant.Slug,
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddMonths(-5),
            ExpiresAt = DateTime.UtcNow.AddMonths(1),
            TokenIssuedAt = DateTime.UtcNow.AddDays(-20),
            TokenExpiresAt = DateTime.UtcNow.AddDays(-5)
        };
        db.Consents.Add(consent);
        db.SaveChanges();

        var tokenOptions = Options.Create(new ConsentTokenOptions());
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), tokenOptions, new TestLogger<JwtConsentTokenFactory>());
        var lifecycleOptions = Options.Create(new ConsentLifecycleOptions { RenewalLeadDays = 7, ExpiryGraceDays = 3 });
        var service = new ConsentLifecycleService(db, factory, lifecycleOptions, new NoopAuditSink());

        var result = await service.RenewAsync(consent.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Renew_Denies_WhenConsentExpired()
    {
        using var db = CreateDb();
        var tenant = SeedAgentTenant(db, "agent_acme");
        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "fay@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);
        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = tenant.Slug,
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddMonths(-6),
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // consent agreement expired
            TokenIssuedAt = DateTime.UtcNow.AddDays(-20),
            TokenExpiresAt = DateTime.UtcNow.AddDays(5)
        };
        db.Consents.Add(consent);
        db.SaveChanges();

        var tokenOptions = Options.Create(new ConsentTokenOptions());
        var factory = new JwtConsentTokenFactory(db, new NoopDataProtectionProvider(), tokenOptions, new TestLogger<JwtConsentTokenFactory>());
        var lifecycleOptions = Options.Create(new ConsentLifecycleOptions { RenewalLeadDays = 7, ExpiryGraceDays = 3 });
        var service = new ConsentLifecycleService(db, factory, lifecycleOptions, new NoopAuditSink());

        var result = await service.RenewAsync(consent.Id);
        result.Should().BeNull();
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

    private sealed class NoopDataProtectionProvider : IDataProtectionProvider, IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class NoopAuditSink : IAuditEventSink
    {
        public Task EmitAsync(AuditEventDescriptor evt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
