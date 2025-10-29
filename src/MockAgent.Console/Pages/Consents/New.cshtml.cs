using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MockAgent.ConsoleApp;
using MockAgent.ConsoleApp.Api;

namespace MockAgent.ConsoleApp.Pages.Consents;

public class NewModel : PageModel
{
    private readonly AgentApiClient _api;
    private readonly DemoState _state;
    private readonly GatewayOptions _opts;

    public NewModel(AgentApiClient api, DemoState state, IOptions<GatewayOptions> opts)
    {
        _api = api;
        _state = state;
        _opts = opts.Value;
        BoardTenantId = _opts.BoardTenantId;
    }

    [BindProperty, Required, EmailAddress]
    public string CandidateEmail { get; set; } = string.Empty;

    [BindProperty]
    public string BoardTenantId { get; set; } = string.Empty;

    public string? RequestId { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var id = await _api.CreateConsentRequestAsync(CandidateEmail, BoardTenantId);
        RequestId = id;
        _state.AddConsent(new ConsentRequestItem(CandidateEmail, id, DateTimeOffset.UtcNow));
        return Page();
    }
}

