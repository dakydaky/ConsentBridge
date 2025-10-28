using System.IO;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "mockboard-ok" }));

app.MapPost("/v1/mock/applications", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    _ = await reader.ReadToEndAsync();
    // Real implementation would verify signature and consent; this is a demo stub.
    var receipt = new
    {
        spec = "consent-apply/v0.1",
        application_id = Guid.NewGuid().ToString(),
        board_id = "mockboard_eu",
        status = "accepted",
        received_at = DateTime.UtcNow,
        board_ref = $"MB-{Random.Shared.Next(100000, 999999)}"
    };
    return Results.Ok(receipt);
});

app.Run("http://0.0.0.0:8081");
