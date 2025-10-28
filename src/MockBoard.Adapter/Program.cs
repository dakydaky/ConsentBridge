using MockBoard.Adapter.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddRazorPages();
builder.Services.AddSingleton<ApplicationFeed>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { status = "mockboard-ok" }));

app.MapPost("/v1/mock/applications", async (HttpRequest req, ApplicationFeed feed) =>
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();
    // Real implementation would verify signature & consent.
    var entry = new MockBoard.Adapter.Services.ApplicationEntry(
        Guid.NewGuid().ToString(),
        "alice@example.com",
        "Backend Engineer",
        "Accepted",
        DateTime.UtcNow
    );
    feed.Add(entry);

    var receipt = new
    {
        spec = "consent-apply/v0.1",
        application_id = entry.Id,
        board_id = "mockboard_eu",
        status = "accepted",
        received_at = entry.ReceivedAt,
        board_ref = $"MB-{Random.Shared.Next(100000, 999999)}"
    };
    return Results.Ok(receipt);
});

app.Run("http://0.0.0.0:8081");

