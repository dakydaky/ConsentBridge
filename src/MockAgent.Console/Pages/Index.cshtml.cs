using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MockAgent.ConsoleApp;
using MockAgent.ConsoleApp.Api;
using Microsoft.Extensions.Options;

namespace MockAgent.ConsoleApp.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly DemoState _state;
    private readonly AgentApiClient _api;
    private readonly GatewayOptions _opts;

    public IndexModel(ILogger<IndexModel> logger, DemoState state, AgentApiClient api, IOptions<GatewayOptions> opts)
    {
        _logger = logger;
        _state = state;
        _api = api;
        _opts = opts.Value;
    }

    public IReadOnlyList<AgentApiClient.ConsentRequestView> PendingRequests { get; private set; } = Array.Empty<AgentApiClient.ConsentRequestView>();
    private HashSet<string> _approvedEmails = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CandidateItem> Candidates => _state.Candidates
        .Where(c => !_approvedEmails.Contains(c.CandidateEmail.Trim().ToLowerInvariant()))
        .ToList();
    public IReadOnlyList<AgentApiClient.ConsentView> Consents { get; private set; } = Array.Empty<AgentApiClient.ConsentView>();
    public IReadOnlyList<ApplicationItem> Applications => _state.Applications;
    public string GatewayBaseUrl => _opts.BaseUrl?.TrimEnd('/') ?? "";

    public async Task OnGet()
    {
        // load latest consents from API for status visibility
        try
        {
            Consents = await _api.GetConsentsAsync(20);
            _approvedEmails = Consents
                .Select(c => c.ApprovedByEmail)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e!.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            PendingRequests = await _api.GetConsentRequestsAsync(status: "Pending", take: 50);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load consents from API");
            Consents = Array.Empty<AgentApiClient.ConsentView>();
            _approvedEmails = new(StringComparer.OrdinalIgnoreCase);
            PendingRequests = Array.Empty<AgentApiClient.ConsentRequestView>();
        }
    }

    public async Task<IActionResult> OnGetData()
    {
        try
        {
            var consents = await _api.GetConsentsAsync(50);
            var pending = await _api.GetConsentRequestsAsync(status: "Pending", take: 50);
            return new JsonResult(new
            {
                consents,
                pending,
                gatewayBaseUrl = GatewayBaseUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard data fetch failed");
            return new JsonResult(new { consents = Array.Empty<object>(), pending = Array.Empty<object>(), gatewayBaseUrl = GatewayBaseUrl });
        }
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id)
    {
        try { await _api.RevokeConsentAsync(id); } catch (Exception ex) { _logger.LogWarning(ex, "Revoke failed"); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenewAsync(Guid id)
    {
        try { await _api.RenewConsentAsync(id); } catch (Exception ex) { _logger.LogWarning(ex, "Renew failed"); }
        return RedirectToPage();
    }
}
