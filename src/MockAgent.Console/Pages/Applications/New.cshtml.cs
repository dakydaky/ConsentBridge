using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
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

    [BindProperty]
    public string ConsentToken { get; set; } = string.Empty;

    [BindProperty]
    public string CoverLetter { get; set; } = string.Empty;

    public string? ResultSnippet { get; set; }
    public List<SelectListItem> ConsentOptions { get; private set; } = new();
    [BindProperty]
    public Guid? SelectedConsentId { get; set; }

    public async Task OnGet()
    {
        await LoadConsentsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await LoadConsentsAsync(); return Page(); }

        if (!SelectedConsentId.HasValue && string.IsNullOrWhiteSpace(ConsentToken))
        {
            ModelState.AddModelError("ConsentToken", "Select an approved consent or paste a consent token (JWT).");
            await LoadConsentsAsync();
            return Page();
        }

        // If the user selected a consent and did not paste a JWT, use ctok:{tokenId}
        if (SelectedConsentId.HasValue && string.IsNullOrWhiteSpace(ConsentToken))
        {
            var chosen = _consentsCache.FirstOrDefault(c => c.Id == SelectedConsentId.Value);
            if (chosen is not null)
            {
                ConsentToken = $"ctok:{chosen.TokenId}";
                if (!string.IsNullOrWhiteSpace(chosen.ApprovedByEmail))
                    CandidateEmail = chosen.ApprovedByEmail!;
            }
        }

        var payload = BuildPayload(ConsentToken, CandidateEmail, JobRef, CoverLetter);
        var response = await _api.SubmitApplicationAsync(payload);
        ResultSnippet = response.Length > 200 ? response.Substring(0, 200) + "…" : response;
        _state.AddApplication(new ApplicationItem(CandidateEmail, JobRef, ResultSnippet ?? string.Empty, DateTimeOffset.UtcNow));
        await LoadConsentsAsync();
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

    private List<AgentApiClient.ConsentView> _consentsCache = new();
    private async Task LoadConsentsAsync()
    {
        try
        {
            _consentsCache = (await _api.GetConsentsAsync(50)).Where(c => string.Equals(c.Status, "Active", StringComparison.OrdinalIgnoreCase)).ToList();
            ConsentOptions = _consentsCache
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = $"{(string.IsNullOrEmpty(c.ApprovedByEmail) ? "(email hidden)" : c.ApprovedByEmail)} · {c.BoardTenantId} · exp {c.TokenExpiresAt.ToLocalTime():g}"
                })
                .ToList();
        }
        catch
        {
            ConsentOptions = new();
            _consentsCache = new();
        }
    }
}
