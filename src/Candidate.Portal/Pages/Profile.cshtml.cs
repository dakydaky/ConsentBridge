using Candidate.PortalApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Candidate.PortalApp.Pages;

public class ProfileModel : PageModel
{
    [BindProperty]
    public CandidateProfile Input { get; set; } = new();

    public IActionResult OnGet()
    {
        var email = HttpContext.Session.GetString("CandidateEmail");
        if (string.IsNullOrWhiteSpace(email)) return RedirectToPage("/Login");
        if (ProfileStore.TryGet(email, out var existing) && existing is not null)
        {
            Input = existing;
        }
        return Page();
    }

    public IActionResult OnPost()
    {
        var email = HttpContext.Session.GetString("CandidateEmail");
        if (string.IsNullOrWhiteSpace(email)) return RedirectToPage("/Login");
        ProfileStore.Save(email, Input);
        return RedirectToPage();
    }
}

