using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Api.Areas.Consent.Pages;

public class CompleteModel : PageModel
{
    private readonly GatewayDbContext _db;

    public CompleteModel(GatewayDbContext db)
    {
        _db = db;
    }

    public string Heading { get; private set; } = "Consent request processed";
    public string Description { get; private set; } = "Thanks for responding.";
    public string? ConsentToken { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var request = await _db.ConsentRequests
            .AsNoTracking()
            .Include(r => r.Consent)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null)
        {
            Heading = "Consent request not found";
            Description = "This link may be invalid or has already been processed.";
            return Page();
        }

        var status = (TempData["ConsentStatus"] as string) ?? request.Status.ToString().ToLowerInvariant();

        switch (status)
        {
            case "approved":
                Heading = "Consent approved";
                Description = "Your agent can now submit applications using this consent token.";
                ConsentToken = TempData["ConsentToken"] as string;
                break;
            case "denied":
                Heading = "Consent denied";
                Description = "Your decision has been recorded. The agent cannot proceed.";
                break;
            case "expired":
                Heading = "Consent request expired";
                Description = "This consent request expired. Please ask your agent to initiate a new one.";
                break;
            default:
                Heading = "Consent request completed";
                Description = "This consent request has already been processed.";
                break;
        }

        return Page();
    }
}
