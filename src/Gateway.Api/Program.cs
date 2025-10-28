using Gateway.Api;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Serilog;
using System.Linq;
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
builder.Services.AddGatewayInfrastructure(builder.Configuration);

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
await DemoTenantSeeder.SeedAsync(app.Services, builder.Configuration);

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

app.MapPost("/oauth/token", async (
    HttpRequest request,
    GatewayDbContext db,
    IClientSecretHasher hasher,
    IAccessTokenFactory tokenFactory,
    [FromBody] ClientCredentialsPayload? payload) =>
{
    string? grantType = null;
    string? clientId = null;
    string? clientSecret = null;
    string? scopeRaw = null;

    if (payload is not null)
    {
        grantType = payload.GrantType;
        clientId = payload.ClientId;
        clientSecret = payload.ClientSecret;
        scopeRaw = payload.Scope;
    }
    else if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync();
        grantType = form["grant_type"].ToString();
        clientId = form["client_id"].ToString();
        clientSecret = form["client_secret"].ToString();
        scopeRaw = form.TryGetValue("scope", out var scopeValue) ? scopeValue.ToString() : null;
    }
    else
    {
        return Results.Json(new { error = "invalid_request", error_description = "Submit as JSON body or application/x-www-form-urlencoded form data." }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (!string.Equals(grantType, "client_credentials", StringComparison.Ordinal))
    {
        return Results.Json(new { error = "unsupported_grant_type" }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        return InvalidClient();
    }

    var credential = await db.TenantCredentials
        .Include(c => c.Tenant)
        .FirstOrDefaultAsync(c => c.ClientId == clientId);
    if (credential?.Tenant is null || !credential.IsActive || !credential.Tenant.IsActive)
    {
        return InvalidClient();
    }

    if (!hasher.Verify(clientSecret, credential.ClientSecretHash))
    {
        return InvalidClient();
    }

    var allowedScopes = credential.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var requestedScopes = !string.IsNullOrWhiteSpace(scopeRaw)
        ? scopeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : Array.Empty<string>();

    if (requestedScopes.Length > 0 && requestedScopes.Except(allowedScopes, StringComparer.Ordinal).Any())
    {
        return Results.Json(new { error = "invalid_scope" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var effectiveScopes = requestedScopes.Length > 0 ? requestedScopes : allowedScopes;
    var token = tokenFactory.IssueToken(credential.Tenant, credential, effectiveScopes);
    var expiresIn = Math.Max(0, (int)Math.Ceiling((token.ExpiresAt - DateTime.UtcNow).TotalSeconds));

    return Results.Json(new
    {
        access_token = token.Token,
        token_type = "Bearer",
        expires_in = expiresIn,
        scope = string.Join(' ', token.Scopes)
    });

    IResult InvalidClient() =>
        Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);

})
   .WithTags("Auth")
   .Accepts<ClientCredentialsPayload>("application/json")
   .WithOpenApi(op =>
   {
       op.Summary = "Client credentials token issuance";
       return op;
   });

app.MapGet("/internal/tenants", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
   .WithTags("Internal")
   .WithOpenApi(op =>
   {
       op.Summary = "Tenant listing (admin) - placeholder";
       return op;
   });

app.Run();
