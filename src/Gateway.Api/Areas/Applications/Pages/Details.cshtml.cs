using System.Text.Encodings.Web;
using System.Text.Json;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Api.Areas.Applications.Pages;

public class DetailsModel : PageModel
{
    private readonly GatewayDbContext _db;

    public DetailsModel(GatewayDbContext db)
    {
        _db = db;
    }

    public ApplicationViewModel? Application { get; private set; }
    public string? ReceiptJson { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var application = await _db.Applications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application is null)
        {
            return NotFound();
        }

        Application = new ApplicationViewModel(
            application.Id,
            application.ConsentId,
            application.Status.ToString(),
            application.SubmittedAt,
            application.AgentTenantId,
            application.BoardTenantId,
            application.PayloadHash,
            application.ReceiptSignature,
            application.ReceiptHash);

        if (!string.IsNullOrWhiteSpace(application.Receipt))
        {
            try
            {
                var doc = JsonDocument.Parse(application.Receipt);
                ReceiptJson = JsonSerializer.Serialize(doc,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
            }
            catch (JsonException)
            {
                ReceiptJson = application.Receipt;
            }
        }

        return Page();
    }

    public sealed record ApplicationViewModel(
        Guid Id,
        Guid ConsentId,
        string Status,
        DateTime SubmittedAt,
        string AgentTenantId,
        string BoardTenantId,
        string PayloadHash,
        string? ReceiptSignature,
        string? ReceiptHash);
}
