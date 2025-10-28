using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Api.Areas.Consent.Pages;

public class RequestModel : PageModel
{
    private readonly GatewayDbContext _db;
    private readonly IClientSecretHasher _hasher;
    private readonly ILogger<RequestModel> _logger;

    public RequestModel(GatewayDbContext db, IClientSecretHasher hasher, ILogger<RequestModel> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    [BindProperty]
    public VerifyInput Input { get; set; } = new();

    public ConsentRequest? RequestEntity { get; private set; }
    public string[] ScopeList { get; private set; } = Array.Empty<string>();
    public string? ErrorMessage { get; private set; }
    public string? InfoMessage { get; private set; }
    public bool ShowVerificationForm => RequestEntity is not null
        && RequestEntity.Status == ConsentRequestStatus.Pending
        && RequestEntity.ExpiresAt > DateTime.UtcNow;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        RequestEntity = await _db.ConsentRequests.FirstOrDefaultAsync(c => c.Id == id);
        if (RequestEntity is null)
        {
            return NotFound();
        }

        if (RequestEntity.ExpiresAt <= DateTime.UtcNow && RequestEntity.Status is ConsentRequestStatus.Pending or ConsentRequestStatus.Verified)
        {
            RequestEntity.Status = ConsentRequestStatus.Expired;
            await _db.SaveChangesAsync();
        }

        if (RequestEntity.Status == ConsentRequestStatus.Verified)
        {
            return RedirectToPage("Approve", new { id });
        }

        if (RequestEntity.Status is ConsentRequestStatus.Approved or ConsentRequestStatus.Denied or ConsentRequestStatus.Expired)
        {
            InfoMessage = "This consent request is no longer active.";
        }

        AssignScopes();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (!ModelState.IsValid)
        {
            return await OnGetAsync(id);
        }

        RequestEntity = await _db.ConsentRequests.FirstOrDefaultAsync(c => c.Id == id);
        if (RequestEntity is null)
        {
            return NotFound();
        }

        AssignScopes();

        if (RequestEntity.ExpiresAt <= DateTime.UtcNow)
        {
            if (RequestEntity.Status != ConsentRequestStatus.Expired)
            {
                RequestEntity.Status = ConsentRequestStatus.Expired;
                await _db.SaveChangesAsync();
            }
            InfoMessage = "This consent request has expired. Ask your agent to start a new one.";
            return Page();
        }

        if (RequestEntity.Status is ConsentRequestStatus.Approved or ConsentRequestStatus.Denied)
        {
            InfoMessage = "This consent request is already completed.";
            return Page();
        }

        if (RequestEntity.Status == ConsentRequestStatus.Verified)
        {
            return RedirectToPage("Approve", new { id });
        }

        var code = Input.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ErrorMessage = "Please enter the code you received.";
            return Page();
        }

        RequestEntity.VerificationAttempts++;

        var verified = RequestEntity.VerificationCodeHash is not null &&
                       _hasher.Verify(code, RequestEntity.VerificationCodeHash);

        if (!verified)
        {
            if (RequestEntity.VerificationAttempts >= 5)
            {
                RequestEntity.Status = ConsentRequestStatus.Denied;
                RequestEntity.DecisionAt = DateTime.UtcNow;
                InfoMessage = "Too many incorrect attempts. This request has been locked.";
            }
            else
            {
                ErrorMessage = "The code is incorrect. Please try again.";
            }

            await _db.SaveChangesAsync();
            return Page();
        }

        RequestEntity.Status = ConsentRequestStatus.Verified;
        RequestEntity.VerifiedAt = DateTime.UtcNow;
        RequestEntity.VerificationCodeHash = null;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Consent request {Id} verified for {Email}", RequestEntity.Id, RequestEntity.CandidateEmail);

        return RedirectToPage("Approve", new { id });
    }

    private void AssignScopes()
    {
        ScopeList = RequestEntity?.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? Array.Empty<string>();
    }

    public class VerifyInput
    {
        public string? Code { get; set; }
    }
}
