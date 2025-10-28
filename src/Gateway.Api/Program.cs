using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Serilog setup
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// DbContext + infrastructure services
builder.Services.AddDbContext<GatewayDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddGatewayInfrastructure();

// Http client for the MockBoard adapter
builder.Services.AddHttpClient("mockboard", client =>
{
    var baseUrl = Environment.GetEnvironmentVariable("MOCKBOARD_URL") ?? "http://mockboard:8081";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations automatically for the demo scenario
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Create Consent (simplified for demo usage)
app.MapPost("/v1/consents", async ([FromBody] CreateConsentDto dto, GatewayDbContext db, IConsentTokenFactory tokenFactory) =>
{
    var normalizedEmail = dto.CandidateEmail.ToLowerInvariant();
    var candidate = await db.Candidates.FirstOrDefaultAsync(c => c.EmailHash == normalizedEmail);
    if (candidate == null)
    {
        candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            EmailHash = normalizedEmail,
            CreatedAt = DateTime.UtcNow
        };
        db.Candidates.Add(candidate);
    }

    var consent = new Consent
    {
        Id = Guid.NewGuid(),
        CandidateId = candidate.Id,
        AgentTenantId = dto.AgentTenantId,
        BoardTenantId = dto.BoardTenantId,
        Status = ConsentStatus.Active,
        IssuedAt = DateTime.UtcNow,
        Scopes = "apply:submit",
        ApprovedByEmail = normalizedEmail
    };

    var issue = tokenFactory.IssueToken(consent, candidate);
    consent.TokenId = issue.TokenId;
    consent.TokenExpiresAt = issue.ExpiresAt;
    consent.ExpiresAt = issue.ExpiresAt;

    db.Consents.Add(consent);
    await db.SaveChangesAsync();

    return Results.Ok(new { consent_token = issue.Token, consent_id = consent.Id });
});

// Submit Application (consent + JWS validation are stubbed for demo)
app.MapPost("/v1/applications", async (
    [FromHeader(Name = "X-JWS-Signature")] string? jws,
    [FromBody] ApplyPayloadDto payload,
    GatewayDbContext db,
    IHttpClientFactory httpFactory) =>
{
    if (payload is null)
    {
        return Results.BadRequest();
    }

    // Validate consent token format
    if (string.IsNullOrWhiteSpace(payload.ConsentToken) || !payload.ConsentToken.StartsWith("ctok:"))
    {
        return Results.Unauthorized();
    }

    if (!Guid.TryParse(payload.ConsentToken.Split(':')[1], out var tokenId))
    {
        return Results.Unauthorized();
    }

    var consent = await db.Consents.Include(c => c.Candidate)
        .FirstOrDefaultAsync(c => c.TokenId == tokenId);
    if (consent is null || consent.Status != ConsentStatus.Active || consent.ExpiresAt <= DateTime.UtcNow || consent.TokenExpiresAt <= DateTime.UtcNow)
    {
        return Results.Forbid();
    }

    var appRec = new Application
    {
        Id = Guid.NewGuid(),
        ConsentId = consent.Id,
        AgentTenantId = consent.AgentTenantId,
        BoardTenantId = consent.BoardTenantId,
        Status = ApplicationStatus.Pending,
        SubmittedAt = DateTime.UtcNow,
        PayloadHash = Guid.NewGuid().ToString("N")
    };
    db.Applications.Add(appRec);
    await db.SaveChangesAsync();

    var client = httpFactory.CreateClient("mockboard");
    var resp = await client.PostAsJsonAsync("/v1/mock/applications", new
    {
        application_id = appRec.Id,
        job_external_id = payload.Job.ExternalId,
        candidate_id = consent.CandidateId,
        payload,
        signature = jws
    });

    if (resp.IsSuccessStatusCode)
    {
        appRec.Status = ApplicationStatus.Accepted;
        appRec.Receipt = await resp.Content.ReadAsStringAsync();
        await db.SaveChangesAsync();
        return Results.Accepted($"/v1/applications/{appRec.Id}", new { id = appRec.Id, status = appRec.Status });
    }

    appRec.Status = ApplicationStatus.Failed;
    await db.SaveChangesAsync();
    return Results.StatusCode(502);
});

app.MapGet("/v1/applications/{id:guid}", async (Guid id, GatewayDbContext db) =>
{
    var application = await db.Applications.FindAsync(id);
    return application is null ? Results.NotFound() : Results.Ok(application);
});

app.MapPost("/v1/consents/{id:guid}/revoke", async (Guid id, GatewayDbContext db) =>
{
    var consent = await db.Consents.FindAsync(id);
    if (consent is null)
    {
        return Results.NotFound();
    }

    consent.Status = ConsentStatus.Revoked;
    consent.RevokedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/oauth/token", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
   .WithTags("Auth")
   .WithOpenApi(op =>
   {
       op.Summary = "Client credentials token issuance (coming soon)";
       return op;
   });

app.MapGet("/internal/tenants", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
   .WithTags("Internal")
   .WithOpenApi(op =>
   {
       op.Summary = "Tenant listing (admin) – placeholder";
       return op;
   });

app.Run();
