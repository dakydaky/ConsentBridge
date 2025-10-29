using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MockAgent.ConsoleApp;
using MockAgent.ConsoleApp.Api;

namespace MockAgent.ConsoleApp.Pages.Applications;

public class NewModel : PageModel
{
    private readonly AgentApiClient _api;
    private readonly DemoState _state;

    public NewModel(AgentApiClient api, DemoState state)
    {
        _api = api;
        _state = state;
        JobRef = "mock:98765";
        CoverLetter = "Hello MockBoard!";
    }

    [BindProperty, Required, EmailAddress]
    public string CandidateEmail { get; set; } = "alice@example.com";

    [BindProperty]
    public string JobRef { get; set; } = string.Empty;

    [BindProperty, Required]
    public string ConsentToken { get; set; } = string.Empty;

    [BindProperty]
    public string CoverLetter { get; set; } = string.Empty;

    public string? ResultSnippet { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var payload = BuildPayload(ConsentToken, CandidateEmail, JobRef, CoverLetter);
        var response = await _api.SubmitApplicationAsync(payload);
        ResultSnippet = response.Length > 200 ? response.Substring(0, 200) + "…" : response;
        _state.AddApplication(new ApplicationItem(CandidateEmail, JobRef, ResultSnippet ?? string.Empty, DateTimeOffset.UtcNow));
        return Page();
    }

    private static string BuildPayload(string token, string email, string jobRef, string coverLetter)
    {
        // Build a minimal payload compatible with Gateway expectations (case-insensitive by default)
        var obj = new
        {
            ConsentToken = token,
            Candidate = new
            {
                Id = "cand_demo",
                Contact = new { Email = email, Phone = "+45 1234" },
                Pii = new { FirstName = "Alice", LastName = "Larsen" },
                Cv = new { Url = "https://example/cv.pdf", Sha256 = "deadbeef" }
            },
            Job = new
            {
                ExternalId = jobRef,
                Title = "Backend Engineer",
                Company = "ACME GmbH",
                ApplyEndpoint = "quick-apply"
            },
            Materials = new
            {
                CoverLetter = new { Text = coverLetter },
                Answers = new[] { new { QuestionId = "q_legal_work", AnswerText = "Yes" } }
            },
            Meta = new { Locale = "de-DE", UserAgent = "agent/0.1", Ts = DateTime.UtcNow.ToString("o") }
        };
        return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}

