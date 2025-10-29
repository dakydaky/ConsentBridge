using Gateway.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure;

public sealed class ConsentLifecycleOptions
{
    public int RenewalLeadDays { get; set; } = 14; // allow renewal when within N days before expiry
    public int ExpiryGraceDays { get; set; } = 7;  // allow renewal up to N days after expiry
}

public sealed class ConsentLifecycleService : IConsentLifecycleService
{
    private readonly GatewayDbContext _db;
    private readonly IConsentTokenFactory _tokenFactory;
    private readonly ConsentLifecycleOptions _options;
    private readonly IAuditEventSink _audit;

    public ConsentLifecycleService(
        GatewayDbContext db,
        IConsentTokenFactory tokenFactory,
        IOptions<ConsentLifecycleOptions> options,
        IAuditEventSink audit)
    {
        _db = db;
        _tokenFactory = tokenFactory;
        _options = options.Value;
        _audit = audit;
    }

    public async Task<ConsentTokenIssueResult?> RenewAsync(Guid consentId, CancellationToken cancellationToken = default)
    {
        var consent = await _db.Consents.Include(c => c.Candidate)
            .FirstOrDefaultAsync(c => c.Id == consentId, cancellationToken);
        if (consent?.Candidate is null)
        {
            return null;
        }

        if (consent.Status != ConsentStatus.Active)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (consent.ExpiresAt <= now)
        {
            return null;
        }
        var windowStart = consent.TokenExpiresAt.AddDays(-Math.Max(0, _options.RenewalLeadDays));
        var windowEnd = consent.TokenExpiresAt.AddDays(Math.Max(0, _options.ExpiryGraceDays));
        if (now < windowStart || now > windowEnd)
        {
            await TryAuditAsync(consent, success: false, reason: "window_violation", cancellationToken: cancellationToken);
            GatewayMetrics.ConsentRenewalsDenied.Add(1, new KeyValuePair<string, object?>("tenant", consent.AgentTenantId));
            return null;
        }

        var result = _tokenFactory.IssueToken(consent, consent.Candidate);
        await _db.SaveChangesAsync(cancellationToken);
        await TryAuditAsync(consent, success: true, jti: result.TokenId.ToString(), cancellationToken: cancellationToken);
        GatewayMetrics.ConsentRenewalsSuccess.Add(1, new KeyValuePair<string, object?>("tenant", consent.AgentTenantId));
        return result;
    }

    private async Task TryAuditAsync(Consent consent, bool success, string? jti = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _audit.EmitAsync(new AuditEventDescriptor(
                Category: "consent",
                Action: success ? "renewal_succeeded" : "renewal_denied",
                EntityType: nameof(Consent),
                EntityId: consent.Id.ToString(),
                Tenant: consent.AgentTenantId,
                CreatedAt: DateTime.UtcNow,
                Jti: jti,
                Metadata: reason), cancellationToken);
        }
        catch
        {
            // best-effort audit
        }
    }
}
