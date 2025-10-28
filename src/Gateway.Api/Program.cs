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
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Threading;

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
builder.Services.AddGatewayInfrastructure(builder.Configuration);

var payloadSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false
};
var jwsHeaderSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
};

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
        options.MapInboundClaims = false;
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

var keyPath = builder.Configuration.GetValue<string>("DataProtection:KeyPath") ?? "/app/dataprotection";
if (!Path.IsPathRooted(keyPath))
{
    keyPath = Path.Combine(builder.Environment.ContentRootPath, keyPath);
}
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("ConsentBridge.Gateway");

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

app.MapGet("/.well-known/jwks.json", (ITenantKeyStore keyStore) =>
{
    var response = new
    {
        keys = keyStore.GetAll()
            .SelectMany(entry => ProjectKeys(entry.Value, entry.Key))
    };

    return Results.Json(response);
}).WithTags("Public")
  .WithOpenApi(op =>
  {
      op.Summary = "Tenant signing keys (JWKS)";
      return op;
  });

app.MapGet("/tenants/{slug}/jwks.json", (string slug, ITenantKeyStore keyStore) =>
{
    if (!keyStore.TryGetKeys(slug, out var jwks) || jwks is null)
    {
        return Results.NotFound();
    }

    var keys = ProjectKeys(jwks, slug).ToArray();
    if (keys.Length == 0)
    {
        return Results.NotFound();
    }

    return Results.Json(new { keys });
}).WithTags("Public")
  .WithOpenApi(op =>
  {
      op.Summary = "Tenant-specific signing keys (JWKS)";
      return op;
  });

app.MapPost("/v1/dsr/export", async (
    ClaimsPrincipal user,
    [FromBody] DsrRequestDto request,
    IDsrService dsrService,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.CandidateEmail))
    {
        return Results.BadRequest(new { error = "invalid_request", error_description = "candidateEmail is required." });
    }

    var (tenantSlug, tenantType) = GetTenantContext(user);
    if (string.IsNullOrWhiteSpace(tenantSlug))
    {
        return Results.Unauthorized();
    }

    var result = await dsrService.ExportAsync(tenantSlug, tenantType, request.CandidateEmail, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization("apply.submit")
  .WithTags("DSR")
  .WithOpenApi(op =>
  {
      op.Summary = "Export candidate data for DSR";
      return op;
  });

app.MapPost("/v1/dsr/delete", async (
    ClaimsPrincipal user,
    [FromBody] DsrDeleteRequestDto request,
    IDsrService dsrService,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.CandidateEmail))
    {
        return Results.BadRequest(new { error = "invalid_request", error_description = "candidateEmail is required." });
    }

    if (!request.Confirm)
    {
        return Results.BadRequest(new { error = "confirmation_required", error_description = "Set confirm=true to perform deletion." });
    }

    var (tenantSlug, tenantType) = GetTenantContext(user);
    if (string.IsNullOrWhiteSpace(tenantSlug))
    {
        return Results.Unauthorized();
    }

    var result = await dsrService.DeleteAsync(tenantSlug, tenantType, request.CandidateEmail, cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization("apply.submit")
  .WithTags("DSR")
  .WithOpenApi(op =>
  {
      op.Summary = "Delete candidate data for DSR";
      return op;
  });

// Create Consent (simplified for demo usage)

// Create consent request (agent-triggered)
app.MapPost("/v1/consent-requests", async (
    ClaimsPrincipal user,
    [FromBody] CreateConsentRequestDto dto,
    GatewayDbContext db,
    IClientSecretHasher hasher,
    ILogger<Program> logger) =>
{
    var (tenantSlug, tenantType) = GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
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

    var scopesList = dto.Scopes?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    var scopes = scopesList is { Length: > 0 }
        ? string.Join(' ', scopesList!)
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

app.MapGet("/v1/consents", async (
    ClaimsPrincipal user,
    [FromQuery] int? take,
    GatewayDbContext db) =>
{
    var (tenantSlug, tenantType) = GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
    {
        return Results.Forbid();
    }

    var pageSize = Math.Clamp(take ?? 20, 1, 100);
    var consents = await db.Consents.AsNoTracking()
        .Where(c => c.AgentTenantId == tenantSlug)
        .OrderByDescending(c => c.IssuedAt)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(consents.Select(MapConsent));
}).RequireAuthorization("apply.submit");

app.MapGet("/v1/consents/{id:guid}", async (
    ClaimsPrincipal user,
    Guid id,
    GatewayDbContext db) =>
{
    var (tenantSlug, tenantType) = GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
    {
        return Results.Forbid();
    }

    var consent = await db.Consents.AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == id);

    if (consent is null || !string.Equals(consent.AgentTenantId, tenantSlug, StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    return Results.Ok(MapConsent(consent));
}).RequireAuthorization("apply.submit");

// Submit Application (consent + JWS validation are stubbed for demo)
app.MapPost("/v1/applications", async (
    [FromHeader(Name = "X-JWS-Signature")] string? jws,
    [FromBody] ApplyPayloadDto payload,
    GatewayDbContext db,
    IHttpClientFactory httpFactory,
    IJwsVerifier verifier,
    ILogger<Program> logger) =>
{
    if (payload is null)
    {
        return Results.BadRequest();
    }

    if (string.IsNullOrWhiteSpace(jws))
    {
        return Results.BadRequest(new { error = "missing_signature" });
    }

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

    var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, payloadSerializerOptions);
    if (!TryParseDetachedJws(jws, payloadBytes, out var submissionHeader))
    {
        return Results.BadRequest(new { error = "invalid_signature" });
    }
    if (!verifier.VerifyDetached(payloadBytes, jws, consent.AgentTenantId))
    {
        return Results.BadRequest(new { error = "invalid_signature" });
    }

    var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes));

    var appRec = new Application
    {
        Id = Guid.NewGuid(),
        ConsentId = consent.Id,
        AgentTenantId = consent.AgentTenantId,
        BoardTenantId = consent.BoardTenantId,
        Status = ApplicationStatus.Pending,
        SubmittedAt = DateTime.UtcNow,
        SubmissionSignature = jws,
        SubmissionKeyId = submissionHeader.Kid,
        SubmissionAlgorithm = submissionHeader.Alg,
        PayloadHash = payloadHash
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
        BoardReceiptEnvelope? envelope = null;
        try
        {
            envelope = await resp.Content.ReadFromJsonAsync<BoardReceiptEnvelope>(ReceiptJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize board receipt for application {ApplicationId}", appRec.Id);
        }

        if (envelope?.Receipt is null || string.IsNullOrWhiteSpace(envelope.ReceiptSignature))
        {
            logger.LogWarning("Board receipt missing payload or signature for application {ApplicationId}", appRec.Id);
        }
        else
        {
            var receiptBytes = ReceiptJson.SerializePayload(envelope.Receipt);
            if (verifier.VerifyDetached(receiptBytes, envelope.ReceiptSignature, consent.BoardTenantId))
            {
                appRec.Status = ApplicationStatus.Accepted;
                appRec.Receipt = Encoding.UTF8.GetString(receiptBytes);
                appRec.ReceiptSignature = envelope.ReceiptSignature;
                appRec.ReceiptHash = Convert.ToHexString(SHA256.HashData(receiptBytes));
                await db.SaveChangesAsync();
                return Results.Accepted($"/v1/applications/{appRec.Id}", new { id = appRec.Id, status = appRec.Status });
            }

            logger.LogWarning("Receipt signature validation failed for application {ApplicationId}", appRec.Id);
        }
    }

    appRec.Status = ApplicationStatus.Failed;
    await db.SaveChangesAsync();
    return Results.StatusCode(502);
}).RequireAuthorization("apply.submit")
  .WithTags("Applications")
  .WithOpenApi(op =>
  {
      op.Summary = "Submit a signed application payload";
      return op;
  });

app.MapGet("/v1/applications/{id:guid}", async Task<Results<NotFound, Ok<ApplicationRecordDto>>> (Guid id, GatewayDbContext db) =>
{
    var application = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    return application is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(MapApplication(application));
}).WithTags("Applications")
  .WithOpenApi(op =>
  {
      op.Summary = "Retrieve application status and receipt metadata";
      return op;
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

string GenerateVerificationCode()
{
    var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
    return value.ToString("D6");
}

(string? slug, TenantType? type) GetTenantContext(ClaimsPrincipal user)
{
    var slug = user.FindFirstValue("sub");
    var typeValue = user.FindFirstValue("tenant_type");
    if (string.IsNullOrWhiteSpace(slug))
    {
        return (null, null);
    }

    return Enum.TryParse<TenantType>(typeValue, out var parsed)
        ? (slug, parsed)
        : (slug, null);
}

bool HasScope(ClaimsPrincipal user, string scope)
{
    IEnumerable<Claim> EnumerateScopeClaims(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.FindAll("scope"))
        {
            yield return claim;
        }
        foreach (var claim in principal.FindAll("http://schemas.microsoft.com/ws/2008/06/identity/claims/scope"))
        {
            yield return claim;
        }
    }

    foreach (var claim in EnumerateScopeClaims(user))
    {
        var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (scopes.Any(s => string.Equals(s, scope, StringComparison.Ordinal)))
        {
            return true;
        }
    }

    return false;
}

bool TryParseDetachedJws(string jws, ReadOnlySpan<byte> canonicalJson, out JwsHeader header)
{
    header = default!;
    var parts = jws.Split('.');
    if (parts.Length != 3)
    {
        return false;
    }

    var headerEncoded = parts[0];
    var payloadEncoded = parts[1];
    var expectedPayload = Base64UrlEncoder.Encode(canonicalJson.ToArray());
    if (!string.Equals(payloadEncoded, expectedPayload, StringComparison.Ordinal))
    {
        return false;
    }

    try
    {
        var headerJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(headerEncoded));
        header = JsonSerializer.Deserialize<JwsHeader>(headerJson, jwsHeaderSerializerOptions)
            ?? throw new JsonException("Invalid JWS header.");
        return !string.IsNullOrWhiteSpace(header.Alg);
    }
    catch
    {
        return false;
    }
}

IEnumerable<object> ProjectKeys(JsonWebKeySet jwks, string tenant)
{
    if (jwks?.Keys is null)
    {
        yield break;
    }

    foreach (var key in jwks.Keys)
    {
        yield return new
        {
            tenant,
            kty = key.Kty,
            use = key.Use,
            alg = string.IsNullOrWhiteSpace(key.Alg) ? "ES256" : key.Alg,
            kid = key.Kid,
            crv = key.Crv,
            x = key.X,
            y = key.Y
        };
    }
}

ApplicationRecordDto MapApplication(Application application) =>
    new(
        application.Id,
        application.ConsentId,
        application.AgentTenantId,
        application.BoardTenantId,
        application.Status,
        application.SubmittedAt,
        application.PayloadHash,
        application.SubmissionSignature,
        application.SubmissionKeyId,
        application.SubmissionAlgorithm,
        application.Receipt,
        application.ReceiptSignature,
        application.ReceiptHash);

ConsentViewDto MapConsent(Consent consent) =>
    new(
        consent.Id,
        consent.AgentTenantId,
        consent.BoardTenantId,
        consent.ApprovedByEmail,
        SplitScopes(consent.Scopes),
        consent.Status,
        consent.IssuedAt,
        consent.ExpiresAt,
        consent.TokenExpiresAt,
        consent.TokenId,
        consent.RevokedAt);

IReadOnlyList<string> SplitScopes(string scopes) =>
    string.IsNullOrWhiteSpace(scopes)
        ? Array.Empty<string>()
        : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

sealed record JwsHeader(string? Alg, string? Kid, string? Typ);

