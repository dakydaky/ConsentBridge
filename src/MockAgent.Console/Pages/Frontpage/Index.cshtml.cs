using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MockAgent.ConsoleApp;
using MockAgent.ConsoleApp.Api;

namespace MockAgent.ConsoleApp.Pages.Frontpage;

public class IndexModel : PageModel
{
    private readonly AgentApiClient _api;
    private readonly DemoState _state;
    private readonly GatewayOptions _opts;

    public IndexModel(AgentApiClient api, DemoState state, IOptions<GatewayOptions> opts)
    {
        _api = api;
        _state = state;
        _opts = opts.Value;
        JobRef = "mock:98765";
    }

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string JobRef { get; set; } = string.Empty;

    public string? ConsentLink { get; set; }
    public string? LoggedInEmail { get; set; }
    public List<AgentApiClient.ConsentRequestView>? MyPending { get; set; }
    public List<AgentApiClient.ConsentView>? MyConsents { get; set; }
    public string GatewayBaseUrl => _opts.BaseUrl?.TrimEnd('/') ?? "";

    public async Task OnGet()
    {
        LoggedInEmail = HttpContext.Session.GetString("CandidateEmail");
        if (!string.IsNullOrWhiteSpace(LoggedInEmail))
        {
            MyPending = (await _api.GetConsentRequestsAsync(email: LoggedInEmail, take: 50)).ToList();
            MyConsents = (await _api.GetConsentsAsync(50)).Where(c => string.Equals(c.ApprovedByEmail, LoggedInEmail, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var requestId = await _api.CreateConsentRequestAsync(Email);
        _state.AddCandidate(new CandidateItem(Email, JobRef, DateTimeOffset.UtcNow));
        ConsentLink = $"{_opts.BaseUrl}/consent/{requestId}";
        return RedirectToPage();
    }

    public IActionResult OnGetLogout()
    {
        HttpContext.Session.Remove("CandidateEmail");
        return RedirectToPage();
    }
}
