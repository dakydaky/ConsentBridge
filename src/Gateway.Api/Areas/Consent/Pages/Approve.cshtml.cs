using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Api.Areas.Consent.Pages;

public class ApproveModel : PageModel
{
    private readonly GatewayDbContext _db;
    private readonly IConsentTokenFactory _tokenFactory;
    private readonly ILogger<ApproveModel> _logger;
    private readonly IAuditEventSink _audit;

    public ApproveModel(GatewayDbContext db, IConsentTokenFactory tokenFactory, ILogger<ApproveModel> logger, IAuditEventSink audit)
    {
        _db = db;
        _tokenFactory = tokenFactory;
        _logger = logger;
        _audit = audit;
    }

    public ConsentRequest? RequestEntity { get; private set; }
    public string[] ScopeList { get; private set; } = Array.Empty<string>();
    public string? ErrorMessage { get; private set; }
    public string? InfoMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        await LoadRequest(id);
        if (RequestEntity is null)
        {
            return NotFound();
        }

        if (RequestEntity.Status == ConsentRequestStatus.Pending)
        {
            InfoMessage = "Please verify your email before approving.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, bool approve)
    {
        await LoadRequest(id, track: true);
        if (RequestEntity is null)
        {
            return NotFound();
        }

        if (RequestEntity.ExpiresAt <= DateTime.UtcNow && RequestEntity.Status != ConsentRequestStatus.Expired)
        {
            RequestEntity.Status = ConsentRequestStatus.Expired;
            RequestEntity.DecisionAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["ConsentStatus"] = "expired";
            return RedirectToPage("Complete", new { id });
        }

        if (RequestEntity.Status != ConsentRequestStatus.Verified)
        {
            TempData["ConsentStatus"] = RequestEntity.Status.ToString().ToLowerInvariant();
            return RedirectToPage("Complete", new { id });
        }

        if (!approve)
        {
            RequestEntity.Status = ConsentRequestStatus.Denied;
            RequestEntity.DecisionAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.EmitAsync(new AuditEventDescriptor(
                Category: "consent",
                Action: "denied",
                EntityType: nameof(ConsentRequest),
                EntityId: RequestEntity.Id.ToString(),
                Tenant: RequestEntity.AgentTenantId,
                CreatedAt: DateTime.UtcNow));
            TempData["ConsentStatus"] = "denied";
            return RedirectToPage("Complete", new { id });
        }

        var normalizedEmail = RequestEntity.CandidateEmail.ToLowerInvariant();
        var candidate = await _db.Candidates.FirstOrDefaultAsync(c => c.EmailHash == normalizedEmail);
        if (candidate is null)
        {
            candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                EmailHash = normalizedEmail,
                CreatedAt = DateTime.UtcNow
            };
            _db.Candidates.Add(candidate);
        }

        var issuedAt = DateTime.UtcNow;
        var consent = new Gateway.Domain.Consent
        {
            Id = Guid.NewGuid(),
            CandidateId = candidate.Id,
            AgentTenantId = RequestEntity.AgentTenantId,
            BoardTenantId = RequestEntity.BoardTenantId,
            Status = ConsentStatus.Active,
            IssuedAt = issuedAt,
            ExpiresAt = issuedAt.AddMonths(6),
            TokenExpiresAt = issuedAt,
            Scopes = RequestEntity.Scopes,
            ApprovedByEmail = normalizedEmail
        };

        var issued = _tokenFactory.IssueToken(consent, candidate);
        consent.TokenId = issued.TokenId;
        consent.TokenIssuedAt = issued.IssuedAt;
        consent.TokenExpiresAt = issued.ExpiresAt;
        consent.TokenKeyId = issued.KeyId;
        consent.TokenAlgorithm = issued.Algorithm;
        consent.TokenHash = issued.TokenHash;
        consent.ExpiresAt = issued.ExpiresAt;

        _db.Consents.Add(consent);

        RequestEntity.Status = ConsentRequestStatus.Approved;
        RequestEntity.DecisionAt = DateTime.UtcNow;
        RequestEntity.ConsentId = consent.Id;
        RequestEntity.VerificationCodeHash = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Consent request {RequestId} approved. Consent {ConsentId}.", RequestEntity.Id, consent.Id);

        await _audit.EmitAsync(new AuditEventDescriptor(
            Category: "consent",
            Action: "issued",
            EntityType: nameof(Consent),
            EntityId: consent.Id.ToString(),
            Tenant: consent.AgentTenantId,
            CreatedAt: DateTime.UtcNow,
            Jti: consent.TokenId.ToString()));

        TempData["ConsentStatus"] = "approved";
        TempData["ConsentToken"] = issued.Token;
        TempData["ConsentExpires"] = issued.ExpiresAt.ToString("O");
        return RedirectToPage("Complete", new { id });
    }

    private async Task LoadRequest(Guid id, bool track = false)
    {
        RequestEntity = track
            ? await _db.ConsentRequests.FirstOrDefaultAsync(c => c.Id == id)
            : await _db.ConsentRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

        ScopeList = RequestEntity?.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? Array.Empty<string>();
    }
}
