using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MockAgent.ConsoleApp;
using MockAgent.ConsoleApp.Api;

namespace MockAgent.ConsoleApp.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly DemoState _state;
    private readonly AgentApiClient _api;

    public IndexModel(ILogger<IndexModel> logger, DemoState state, AgentApiClient api)
    {
        _logger = logger;
        _state = state;
        _api = api;
    }

    public IReadOnlyList<ConsentRequestItem> PendingRequests => _state.Consents;
    public IReadOnlyList<AgentApiClient.ConsentView> Consents { get; private set; } = Array.Empty<AgentApiClient.ConsentView>();
    public IReadOnlyList<ApplicationItem> Applications => _state.Applications;

    public async Task OnGet()
    {
        // load latest consents from API for status visibility
        try
        {
            Consents = await _api.GetConsentsAsync(20);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load consents from API");
            Consents = Array.Empty<AgentApiClient.ConsentView>();
        }
    }
}
