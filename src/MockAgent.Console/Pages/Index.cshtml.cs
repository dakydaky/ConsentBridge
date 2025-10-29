using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MockAgent.ConsoleApp;

namespace MockAgent.ConsoleApp.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly DemoState _state;

    public IndexModel(ILogger<IndexModel> logger, DemoState state)
    {
        _logger = logger;
        _state = state;
    }

    public IReadOnlyList<ConsentRequestItem> Consents => _state.Consents;
    public IReadOnlyList<ApplicationItem> Applications => _state.Applications;

    public void OnGet()
    {

    }
}
