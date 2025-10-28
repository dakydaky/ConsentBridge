using Microsoft.AspNetCore.Mvc.RazorPages;
using MockBoard.Adapter.Services;

namespace MockBoard.Adapter.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationFeed _feed;

    public IndexModel(ApplicationFeed feed)
    {
        _feed = feed;
    }

    public IReadOnlyList<ApplicationEntry> Applications { get; private set; } = Array.Empty<ApplicationEntry>();

    public void OnGet()
    {
        Applications = _feed.ListApplications();
    }
}
