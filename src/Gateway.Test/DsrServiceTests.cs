using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace Gateway.Test;

public class DsrServiceTests
{
    [Fact]
    public async Task ExportAsync_ReturnsScopedData_ForAgentTenant()
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GatewayDbContext(options);
        var service = new DsrService(db);

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "alice@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);

        var consent1 = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = "agent_acme",
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddMonths(1)
        };
        var consent2 = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = "other_agent",
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddDays(-2),
            ExpiresAt = DateTime.UtcNow.AddMonths(1)
        };
        db.Consents.AddRange(consent1, consent2);

        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            ConsentId = consent1.Id,
            AgentTenantId = consent1.AgentTenantId,
            BoardTenantId = consent1.BoardTenantId,
            Status = ApplicationStatus.Accepted,
            SubmittedAt = DateTime.UtcNow.AddHours(-2),
            PayloadHash = "hash",
            ReceiptHash = "receipt-hash"
        });

        db.ConsentRequests.Add(new ConsentRequest
        {
            Id = Guid.NewGuid(),
            AgentTenantId = consent1.AgentTenantId,
            BoardTenantId = consent1.BoardTenantId,
            CandidateEmail = candidate.EmailHash,
            Scopes = "apply.submit",
            Status = ConsentRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            VerifiedAt = DateTime.UtcNow.AddDays(-3),
            DecisionAt = DateTime.UtcNow.AddDays(-3)
        });

        await db.SaveChangesAsync();

        var result = await service.ExportAsync("agent_acme", TenantType.Agent, "alice@example.com");

        result.Should().NotBeNull();
        result!.Consents.Should().HaveCount(1);
        result.Applications.Should().HaveCount(1);
        result.ConsentRequests.Should().HaveCount(1);
        result.Consents.Single().AgentTenantId.Should().Be("agent_acme");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTenantScopedData_AndLeavesOthers()
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GatewayDbContext(options);
        var service = new DsrService(db);

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "bob@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);

        var tenantConsent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = "agent_acme",
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddMonths(1)
        };
        var otherConsent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = "other_agent",
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddDays(-2),
            ExpiresAt = DateTime.UtcNow.AddMonths(1)
        };
        db.Consents.AddRange(tenantConsent, otherConsent);

        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            ConsentId = tenantConsent.Id,
            AgentTenantId = tenantConsent.AgentTenantId,
            BoardTenantId = tenantConsent.BoardTenantId,
            Status = ApplicationStatus.Accepted,
            SubmittedAt = DateTime.UtcNow.AddHours(-5),
            PayloadHash = "hash"
        });

        db.ConsentRequests.Add(new ConsentRequest
        {
            Id = Guid.NewGuid(),
            AgentTenantId = tenantConsent.AgentTenantId,
            BoardTenantId = tenantConsent.BoardTenantId,
            CandidateEmail = candidate.EmailHash,
            Scopes = "apply.submit",
            Status = ConsentRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            ExpiresAt = DateTime.UtcNow.AddDays(10)
        });

        await db.SaveChangesAsync();

        var deleteResult = await service.DeleteAsync("agent_acme", TenantType.Agent, "bob@example.com");

        deleteResult.ConsentsDeleted.Should().Be(1);
        deleteResult.ApplicationsDeleted.Should().Be(1);
        deleteResult.ConsentRequestsDeleted.Should().Be(1);
        deleteResult.CandidateDeleted.Should().BeFalse();

        var remainingConsents = await db.Consents.AsNoTracking().Where(c => c.CandidateId == candidate.Id).ToListAsync();
        remainingConsents.Should().HaveCount(1);
        remainingConsents.Single().AgentTenantId.Should().Be("other_agent");
    }

    [Fact]
    public async Task DeleteAsync_RemovesCandidate_WhenNoDataRemains()
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GatewayDbContext(options);
        var service = new DsrService(db);

        var candidate = new Candidate { Id = Guid.NewGuid(), EmailHash = "charlie@example.com", CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(candidate);

        var consent = new Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = "agent_acme",
            BoardTenantId = "mockboard_eu",
            Scopes = "apply.submit",
            Status = ConsentStatus.Active,
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddMonths(1)
        };
        db.Consents.Add(consent);

        await db.SaveChangesAsync();

        var deleteResult = await service.DeleteAsync("agent_acme", TenantType.Agent, "charlie@example.com");

        deleteResult.CandidateDeleted.Should().BeTrue();
        (await db.Candidates.AsNoTracking().AnyAsync(c => c.EmailHash == "charlie@example.com")).Should().BeFalse();
    }
}
