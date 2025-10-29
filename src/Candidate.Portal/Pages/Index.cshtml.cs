using System.ComponentModel.DataAnnotations;
using Candidate.PortalApp.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Candidate.PortalApp.Pages;

public class IndexModel : PageModel
{
    private readonly AgentApiClient _api;
    private readonly Candidate.PortalApp.GatewayOptions _opts;

    public IndexModel(AgentApiClient api, IOptions<Candidate.PortalApp.GatewayOptions> opts)
    {
        _api = api;
        _opts = opts.Value;
        JobRef = "mock:98765";
    }

    public string? LoggedInEmail { get; set; }

    [BindProperty]
    public string JobRef { get; set; } = string.Empty;

    public string? ConsentLink { get; set; }
    public List<AgentApiClient.ConsentRequestView> MyPending { get; set; } = new();
    public List<AgentApiClient.ConsentView> MyConsents { get; set; } = new();
    public List<AgentApiClient.ApplicationRecord> MyApplications { get; set; } = new();
    public string PublicBaseUrl => (_opts.PublicBaseUrl ?? _opts.BaseUrl)?.TrimEnd('/') ?? "";

    public async Task OnGet()
    {
        LoggedInEmail = HttpContext.Session.GetString("CandidateEmail");
        if (!string.IsNullOrWhiteSpace(LoggedInEmail))
        {
            var pending = await _api.GetConsentRequestsAsync(LoggedInEmail);
            MyPending = pending.ToList();
            var consents = await _api.GetConsentsAsync();
            MyConsents = consents.Where(c => string.Equals(c.ApprovedByEmail, LoggedInEmail, StringComparison.OrdinalIgnoreCase)).ToList();
            MyApplications = (await _api.GetApplicationsAsync(LoggedInEmail)).ToList();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoggedInEmail = HttpContext.Session.GetString("CandidateEmail");
        if (string.IsNullOrWhiteSpace(LoggedInEmail))
        {
            return RedirectToPage("/Login");
        }
        var id = await _api.CreateConsentRequestAsync(LoggedInEmail);
        ConsentLink = $"{PublicBaseUrl}/consent/{id}";
        return RedirectToPage();
    }

    public IActionResult OnGetLogout()
    {
        HttpContext.Session.Remove("CandidateEmail");
        return RedirectToPage();
    }
}
