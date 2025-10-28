using System;
using System.Threading.Tasks;
using FluentAssertions;
using System.Linq;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gateway.Test;

public class RetentionExecutorTests
{
    [Fact]
    public async Task RunAsync_ClearsReceiptsAndDeletesConsentRequests()
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new GatewayDbContext(options);
        var now = DateTime.UtcNow;

        db.Applications.Add(new Application
        {
            Id = Guid.NewGuid(),
            ConsentId = Guid.NewGuid(),
            AgentTenantId = "agent_acme",
            BoardTenantId = "mockboard_eu",
            Status = ApplicationStatus.Accepted,
            SubmittedAt = now.AddDays(-400),
            PayloadHash = "hash",
            Receipt = "payload"
        });

        db.ConsentRequests.Add(new ConsentRequest
        {
            Id = Guid.NewGuid(),
            AgentTenantId = "agent_acme",
            BoardTenantId = "mockboard_eu",
            CandidateEmail = "alice@example.com",
            Scopes = "apply.submit",
            Status = ConsentRequestStatus.Approved,
            CreatedAt = now.AddDays(-200),
            ExpiresAt = now.AddDays(-100)
        });

        db.ConsentRequests.Add(new ConsentRequest
        {
            Id = Guid.NewGuid(),
            AgentTenantId = "agent_acme",
            BoardTenantId = "mockboard_eu",
            CandidateEmail = "recent@example.com",
            Scopes = "apply.submit",
            Status = ConsentRequestStatus.Approved,
            CreatedAt = now,
            ExpiresAt = now.AddDays(1)
        });

        await db.SaveChangesAsync();

        var executor = new DataRetentionExecutor(
            db,
            Options.Create(new RetentionOptions
            {
                ApplicationReceiptDays = 365,
                ConsentRequestDays = 90
            }),
            NullLogger<DataRetentionExecutor>.Instance);

        var result = await executor.RunAsync();

        result.ReceiptsCleared.Should().Be(1);
        result.ConsentRequestsDeleted.Should().Be(1);

        var application = await db.Applications.AsNoTracking().FirstAsync();
        application.Receipt.Should().BeNull();

        var consentRequests = await db.ConsentRequests.AsNoTracking().ToListAsync();
        consentRequests.Should().HaveCount(1);
        consentRequests.Single().CandidateEmail.Should().Be("recent@example.com");
    }
}

