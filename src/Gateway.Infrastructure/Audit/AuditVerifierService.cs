using System.Security.Cryptography;
using System.Text;
using Gateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure;

public sealed class AuditVerifierService : IAuditVerifier
{
    private readonly GatewayDbContext _db;

    public AuditVerifierService(GatewayDbContext db)
    {
        _db = db;
    }

    public async Task<AuditVerificationResult> VerifyAsync(string tenantSlug, DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug)) throw new ArgumentException("tenantSlug required", nameof(tenantSlug));
        if (windowEndUtc <= windowStartUtc) throw new ArgumentException("windowEndUtc must be after windowStartUtc");

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug, cancellationToken);
        if (tenant is null)
        {
            return new AuditVerificationResult(false, tenantSlug, windowStartUtc, windowEndUtc, string.Empty, string.Empty, null, "tenant_not_found");
        }

        var prev = await _db.AuditEventHashes.AsNoTracking()
            .Where(h => h.TenantChainId == tenant.Id && h.CreatedAt <= windowStartUtc)
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var previousHash = prev?.CurrentHash ?? "GENESIS";

        var events = await _db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant.Id && e.CreatedAt >= windowStartUtc && e.CreatedAt <= windowEndUtc)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        Guid? firstMismatch = null;
        string computedHash = previousHash;
        foreach (var e in events)
        {
            var canonical = Canonicalize(e, computedHash);
            var expected = ComputeSha256Hex(canonical);
            var link = await _db.AuditEventHashes.AsNoTracking().FirstOrDefaultAsync(h => h.EventId == e.Id, cancellationToken);
            if (link is null || !string.Equals(link.CurrentHash, expected, StringComparison.Ordinal))
            {
                firstMismatch = e.Id;
                computedHash = expected;
                break;
            }
            computedHash = expected;
        }

        var success = firstMismatch is null;
        var run = new AuditVerificationRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            WindowStartUtc = windowStartUtc,
            WindowEndUtc = windowEndUtc,
            PreviousHash = previousHash,
            ComputedHash = computedHash,
            Success = success,
            Error = success ? null : $"mismatch_at:{firstMismatch}",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.AuditVerificationRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        return new AuditVerificationResult(success, tenantSlug, windowStartUtc, windowEndUtc, previousHash, computedHash, firstMismatch, run.Error);
    }

    public async Task<IReadOnlyList<AuditVerificationRunDto>> GetRecentRunsAsync(string tenantSlug, int take = 10, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug, cancellationToken);
        if (tenant is null) return Array.Empty<AuditVerificationRunDto>();
        var rows = await _db.AuditVerificationRuns.AsNoTracking()
            .Where(v => v.TenantId == tenant.Id)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
        return rows.Select(r => new AuditVerificationRunDto(
            r.Id,
            tenantSlug,
            r.WindowStartUtc,
            r.WindowEndUtc,
            r.Success,
            r.PreviousHash,
            r.ComputedHash,
            r.CreatedAtUtc,
            r.Error)).ToList();
    }

    private static string Canonicalize(AuditEvent e, string prev)
    {
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

