using System.Text.Json;
using Gateway.Domain;
using Microsoft.Extensions.Options;
using MockBoard.Adapter;
using MockBoard.Adapter.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddRazorPages();
builder.Services.Configure<MockBoardOptions>(builder.Configuration.GetSection("MockBoard"));
builder.Services.AddSingleton<ApplicationFeed>();
builder.Services.AddSingleton<ReceiptSigner>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { status = "mockboard-ok" }));

app.MapPost("/v1/mock/applications", (
    ApplicationSubmission submission,
    ApplicationFeed feed,
    ReceiptSigner signer,
    IOptions<MockBoardOptions> boardOptions) =>
{
    var receivedAt = DateTime.UtcNow;
    var candidateEmail = TryGetCandidateEmail(submission.Payload) ?? "alice@example.com";
    var jobTitle = TryGetJobTitle(submission.Payload) ?? "Backend Engineer";
    var payloadDetails = ApplicationPayloadDetails.TryParse(submission.Payload);

    if (payloadDetails is null)
    {
        Log.Warning("Failed to parse application payload for {ApplicationId}", submission.ApplicationId);
    }

    var entry = new ApplicationEntry(
        submission.ApplicationId,
        candidateEmail,
        jobTitle,
        "Accepted",
        receivedAt,
        payloadDetails);
    feed.Add(entry);

    var payload = new BoardReceiptPayload(
        Spec: "consent-apply/v0.1",
        ApplicationId: submission.ApplicationId,
        BoardId: boardOptions.Value.BoardId,
        JobExternalId: submission.JobExternalId,
        CandidateId: submission.CandidateId,
        Status: "accepted",
        ReceivedAt: receivedAt,
        BoardRef: $"MB-{Random.Shared.Next(100000, 999999)}");

    var signature = signer.Sign(payload, out _);
    var envelope = new BoardReceiptEnvelope(payload, signature);
    return Results.Ok(envelope);
});

app.Run("http://0.0.0.0:8081");

static string? TryGetCandidateEmail(JsonElement payload) =>
    payload.TryGetProperty("candidate", out var candidate) &&
    candidate.TryGetProperty("contact", out var contact) &&
    contact.TryGetProperty("email", out var email) &&
    email.ValueKind == JsonValueKind.String
        ? email.GetString()
        : null;

static string? TryGetJobTitle(JsonElement payload) =>
    payload.TryGetProperty("job", out var job) &&
    job.TryGetProperty("title", out var title) &&
    title.ValueKind == JsonValueKind.String
        ? title.GetString()
        : null;

