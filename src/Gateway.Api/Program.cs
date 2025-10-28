using System.Security.Claims;
using System.Security.Cryptography;
using Gateway.Api;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Linq;
using System.Net.Http.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Serilog setup
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// DbContext + infrastructure services
builder.Services.AddDbContext<GatewayDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.Configure<JwtAccessTokenOptions>(builder.Configuration.GetSection("Auth:Jwt"));
var jwtOptions = builder.Configuration.GetSection("Auth:Jwt").Get<JwtAccessTokenOptions>()
    ?? throw new InvalidOperationException("Auth:Jwt configuration missing.");
builder.Services.AddGatewayInfrastructure();

// Http client for the MockBoard adapter
builder.Services.AddHttpClient("mockboard", client =>
{
    var baseUrl = Environment.GetEnvironmentVariable("MOCKBOARD_URL") ?? "http://mockboard:8081";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ConsentBridge Gateway", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter the bearer token obtained from /oauth/token (e.g., 'Bearer eyJ...')."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("apply.submit", policy =>
        policy.RequireAssertion(ctx => HasScope(ctx.User, "apply.submit")));
});

builder.Services.AddRazorPages();
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

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();


app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Create Consent (simplified for demo usage)

// Create consent request (agent-triggered)
app.MapPost("/v1/consent-requests", async (
    ClaimsPrincipal user,
    [FromBody] CreateConsentRequestDto dto,
    GatewayDbContext db,
    IClientSecretHasher hasher,
    ILogger<Program> logger) =>
{
    var tenantSlug = user.FindFirstValue("sub") ?? string.Empty;
    var tenantType = user.FindFirstValue("tenant_type");
    if (string.IsNullOrEmpty(tenantSlug) || !string.Equals(tenantType, nameof(TenantType.Agent), StringComparison.Ordinal))
    {
        return Results.Forbid();
    }

    if (!string.IsNullOrWhiteSpace(dto.AgentTenantId) && !string.Equals(dto.AgentTenantId, tenantSlug, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "agent_mismatch" });
    }

    var normalizedEmail = dto.CandidateEmail.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalizedEmail))
    {
        return Results.BadRequest(new { error = "invalid_candidate_email" });
    }
    var scopes = dto.Scopes is { Count: > 0 }
        ? string.Join(' ', dto.Scopes)
        : "apply:submit";

    var now = DateTime.UtcNow;
    var request = new ConsentRequest
    {
        Id = Guid.NewGuid(),
        AgentTenantId = tenantSlug,
        BoardTenantId = dto.BoardTenantId,
        CandidateEmail = normalizedEmail,
        Scopes = scopes,
        Status = ConsentRequestStatus.Pending,
        CreatedAt = now,
        ExpiresAt = now.AddHours(24)
    };

    var code = GenerateVerificationCode();
    request.VerificationCodeHash = hasher.HashSecret(code);

    db.ConsentRequests.Add(request);
    await db.SaveChangesAsync();

    logger.LogInformation("Consent request {RequestId} for {Email}: verification code {Code}", request.Id, normalizedEmail, code);

    return Results.Accepted($"/consent/{request.Id}", new
    {
        request_id = request.Id,
        expires_at = request.ExpiresAt,
        link = $"/consent/{request.Id}"
    });
}).RequireAuthorization("apply.submit");

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
}).RequireAuthorization("apply.submit");

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

app.MapRazorPages();
app.Run();

static string GenerateVerificationCode()
{
    var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
    return value.ToString("D6");
}

static bool HasScope(ClaimsPrincipal user, string scope)
{
    var scopeClaims = user.FindAll("scope");
    foreach (var claim in scopeClaims)
    {
        var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (scopes.Any(s => string.Equals(s, scope, StringComparison.Ordinal)))
        {
            return true;
        }
    }

    return false;
}
