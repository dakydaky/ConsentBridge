using System.Security.Cryptography;
using System.Text;
using Gateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure;

public sealed class PersistentAuditEventSink : IAuditEventSink
{
    private readonly GatewayDbContext _db;

    public PersistentAuditEventSink(GatewayDbContext db)
    {
        _db = db;
    }

    public async Task EmitAsync(AuditEventDescriptor evt, CancellationToken cancellationToken = default)
    {
        // Resolve tenant by slug if possible; otherwise, drop event
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == evt.Tenant, cancellationToken);
        if (tenant is null) return;

        var audit = new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Category = evt.Category,
            Action = evt.Action,
            EntityType = evt.EntityType,
            EntityId = evt.EntityId,
            PayloadHash = null,
            CreatedAt = evt.CreatedAt,
            ActorType = null,
            ActorId = null,
            Jti = evt.Jti,
            Metadata = evt.Metadata
        };

        // Hash chain per tenant
        var previous = await _db.AuditEventHashes.AsNoTracking()
            .Where(h => h.TenantChainId == tenant.Id)
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var previousHash = previous?.CurrentHash ?? "GENESIS";
        var canonical = Canonicalize(audit, previousHash);
        var currentHash = ComputeSha256Hex(canonical);

        var link = new AuditEventHash
        {
            EventId = audit.Id,
            TenantChainId = tenant.Id,
            PreviousHash = previousHash,
            CurrentHash = currentHash,
            CreatedAt = audit.CreatedAt
        };

        // Persist the principal first to satisfy FK ordering regardless of model config
        _db.AuditEvents.Add(audit);
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuditEventHashes.Add(link);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string Canonicalize(AuditEvent e, string prev)
    {
        // Deterministic canonical string: pipe-separated ordered fields
        return string.Join('|', new[]
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
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

