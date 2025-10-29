using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gateway.Test;

public class AuditVerifierServiceTests
{
    [Fact]
    public async Task VerifyAsync_ComputesChain_Success()
    {
        using var db = CreateDb();
        // Seed tenant
        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "agent_acme", DisplayName = "agent_acme", Type = TenantType.Agent, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // Seed an application entity id
        var appId = Guid.NewGuid();

        // Seed events and hashes
        var e1 = new AuditEvent { Id = Guid.NewGuid(), TenantId = tenant.Id, Category = "application", Action = "created", EntityType = nameof(Application), EntityId = appId.ToString(), CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        var h0 = new AuditEventHash { EventId = Guid.Empty, TenantChainId = tenant.Id, PreviousHash = "", CurrentHash = "GENESIS", CreatedAt = DateTime.UtcNow.AddMinutes(-3) }; // not linked in DB, just anchor concept
        var prev = "GENESIS";
        var c1 = Canonical(prev, e1);
        var h1 = new AuditEventHash { EventId = e1.Id, TenantChainId = tenant.Id, PreviousHash = prev, CurrentHash = Hex(c1), CreatedAt = e1.CreatedAt };

        var e2 = new AuditEvent { Id = Guid.NewGuid(), TenantId = tenant.Id, Category = "application", Action = "accepted", EntityType = nameof(Application), EntityId = appId.ToString(), CreatedAt = DateTime.UtcNow.AddMinutes(-1) };
        var c2 = Canonical(h1.CurrentHash, e2);
        var h2 = new AuditEventHash { EventId = e2.Id, TenantChainId = tenant.Id, PreviousHash = h1.CurrentHash, CurrentHash = Hex(c2), CreatedAt = e2.CreatedAt };

        db.AuditEvents.AddRange(e1, e2);
        db.AuditEventHashes.AddRange(h1, h2);
        await db.SaveChangesAsync();

        var verifier = new AuditVerifierService(db);
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;
        var result = await verifier.VerifyAsync(tenant.Slug, start, end);

        Assert.True(result.Success);
        Assert.Equal(h2.CurrentHash, result.ComputedHash);
    }

    private static GatewayDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GatewayDbContext(options);
    }

    private static string Canonical(string prev, AuditEvent e)
    {
        var s = string.Join('|', new[]
        {
            prev,
            e.TenantId.ToString(),
            e.Category,
            e.Action,
            e.EntityType,
            e.EntityId,
            e.Jti ?? string.Empty,
            e.CreatedAt.ToUniversalTime().ToString("O"),
            e.Metadata ?? string.Empty,
            e.PayloadHash ?? string.Empty
        });
        return s;
    }

    private static string Hex(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

