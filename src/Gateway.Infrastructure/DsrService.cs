using Gateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure;

public sealed class DsrService : IDsrService
{
    private readonly GatewayDbContext _db;

    public DsrService(GatewayDbContext db)
    {
        _db = db;
    }

    public async Task<DsrExportResult?> ExportAsync(string tenantSlug, TenantType? tenantType, string candidateEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(candidateEmail))
        {
            return null;
        }

        var normalizedEmail = NormalizeEmail(candidateEmail);
        var candidate = await _db.Candidates.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmailHash == normalizedEmail, cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        var consentsQuery = _db.Consents.AsNoTracking()
            .Where(c => c.CandidateId == candidate.Id);
        consentsQuery = FilterConsents(consentsQuery, tenantSlug, tenantType);
        var consents = await consentsQuery.ToListAsync(cancellationToken);

        var consentIds = consents.Select(c => c.Id).ToArray();
        List<Application> applications = consentIds.Length == 0
            ? new List<Application>()
            : await _db.Applications.AsNoTracking()
                .Where(a => consentIds.Contains(a.ConsentId))
                .ToListAsync(cancellationToken);

        var consentRequestsQuery = _db.ConsentRequests.AsNoTracking()
            .Where(cr => cr.CandidateEmail == normalizedEmail);
        consentRequestsQuery = FilterConsentRequests(consentRequestsQuery, tenantSlug, tenantType);
        var consentRequests = await consentRequestsQuery.ToListAsync(cancellationToken);

        var consentRecords = consents.Select(c => new DsrConsentRecord(
            c.Id,
            c.AgentTenantId,
            c.BoardTenantId,
            c.Scopes,
            c.Status,
            c.IssuedAt,
            c.ExpiresAt,
            c.RevokedAt)).ToList();

        var applicationRecords = applications.Select(a => new DsrApplicationRecord(
            a.Id,
            a.ConsentId,
            a.AgentTenantId,
            a.BoardTenantId,
            a.Status.ToString(),
            a.SubmittedAt,
            a.PayloadHash,
            a.SubmissionSignature,
            a.SubmissionKeyId,
            a.SubmissionAlgorithm,
            a.ReceiptSignature,
            a.ReceiptHash)).ToList();

        var consentRequestRecords = consentRequests.Select(cr => new DsrConsentRequestRecord(
            cr.Id,
            cr.AgentTenantId,
            cr.BoardTenantId,
            cr.CandidateEmail,
            cr.Scopes,
            cr.Status,
            cr.CreatedAt,
            cr.ExpiresAt,
            cr.VerifiedAt,
            cr.DecisionAt)).ToList();

        // Include audit events linked to subject's consents/applications/requests
        var consentIdsSet = new HashSet<Guid>(consents.Select(c => c.Id));
        var applicationIdsSet = new HashSet<Guid>(applications.Select(a => a.Id));
        var requestIdsSet = new HashSet<Guid>(consentRequests.Select(r => r.Id));

        var auditEvents = await _db.AuditEvents.AsNoTracking()
            .Where(a =>
                (a.EntityType == nameof(Consent) && consentIdsSet.Contains(Guid.Parse(a.EntityId))) ||
                (a.EntityType == nameof(Application) && applicationIdsSet.Contains(Guid.Parse(a.EntityId))) ||
                (a.EntityType == nameof(ConsentRequest) && requestIdsSet.Contains(Guid.Parse(a.EntityId))))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var auditRecords = auditEvents.Select(a => new DsrAuditEventRecord(
            a.Id,
            a.Category,
            a.Action,
            a.EntityType,
            a.EntityId,
            a.CreatedAt,
            a.Jti,
            a.Metadata)).ToList();

        return new DsrExportResult(
            normalizedEmail,
            candidate.CreatedAt,
            consentRecords,
            applicationRecords,
            consentRequestRecords,
            auditRecords);
    }

    public async Task<DsrDeleteResult> DeleteAsync(string tenantSlug, TenantType? tenantType, string candidateEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(candidateEmail))
        {
            return new DsrDeleteResult(0, 0, 0, false);
        }

        var normalizedEmail = NormalizeEmail(candidateEmail);
        var candidate = await _db.Candidates
            .FirstOrDefaultAsync(c => c.EmailHash == normalizedEmail, cancellationToken);

        if (candidate is null)
        {
            return new DsrDeleteResult(0, 0, 0, false);
        }

        var consentsQuery = _db.Consents
            .Where(c => c.CandidateId == candidate.Id);
        consentsQuery = FilterConsents(consentsQuery, tenantSlug, tenantType);
        var consents = await consentsQuery.ToListAsync(cancellationToken);

        var consentIds = consents.Select(c => c.Id).ToArray();
        List<Application> applications = consentIds.Length == 0
            ? new List<Application>()
            : await _db.Applications
                .Where(a => consentIds.Contains(a.ConsentId))
                .ToListAsync(cancellationToken);

        var consentRequestsQuery = _db.ConsentRequests
            .Where(cr => cr.CandidateEmail == normalizedEmail);
        consentRequestsQuery = FilterConsentRequests(consentRequestsQuery, tenantSlug, tenantType);
        var consentRequests = await consentRequestsQuery.ToListAsync(cancellationToken);

        _db.Applications.RemoveRange(applications);
        _db.Consents.RemoveRange(consents);
        _db.ConsentRequests.RemoveRange(consentRequests);

        await _db.SaveChangesAsync(cancellationToken);

        var candidateDeleted = await TryDeleteCandidateAsync(candidate, normalizedEmail, cancellationToken);

        return new DsrDeleteResult(
            consents.Count,
            applications.Count,
            consentRequests.Count,
            candidateDeleted);
    }

    private static IQueryable<Consent> FilterConsents(IQueryable<Consent> query, string tenantSlug, TenantType? tenantType) =>
        tenantType switch
        {
            TenantType.Board => query.Where(c => c.BoardTenantId == tenantSlug),
            _ => query.Where(c => c.AgentTenantId == tenantSlug)
        };

    private static IQueryable<ConsentRequest> FilterConsentRequests(IQueryable<ConsentRequest> query, string tenantSlug, TenantType? tenantType) =>
        tenantType switch
        {
            TenantType.Board => query.Where(cr => cr.BoardTenantId == tenantSlug),
            _ => query.Where(cr => cr.AgentTenantId == tenantSlug)
        };

    private async Task<bool> TryDeleteCandidateAsync(Candidate candidate, string normalizedEmail, CancellationToken cancellationToken)
    {
        var hasConsents = await _db.Consents.AsNoTracking()
            .AnyAsync(c => c.CandidateId == candidate.Id, cancellationToken);
        if (hasConsents)
        {
            return false;
        }

        var hasConsentRequests = await _db.ConsentRequests.AsNoTracking()
            .AnyAsync(cr => cr.CandidateEmail == normalizedEmail, cancellationToken);
        if (hasConsentRequests)
        {
            return false;
        }

        _db.Candidates.Remove(candidate);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
