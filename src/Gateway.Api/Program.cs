using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Gateway.Api;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddDbContext<GatewayDbContext>(opts =>
{
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    // Avoid crashing migrator on dev when model changes exist but migrations are being rebuilt.
    opts.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});
builder.Services.Configure<JwtAccessTokenOptions>(builder.Configuration.GetSection("Auth:Jwt"));
var jwtOptions = builder.Configuration.GetSection("Auth:Jwt").Get<JwtAccessTokenOptions>()
    ?? throw new InvalidOperationException("Auth:Jwt configuration missing.");
builder.Services.AddGatewayInfrastructure(builder.Configuration);

var payloadSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false
};

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
        policy.RequireAssertion(ctx => AuthHelpers.HasScope(ctx.User, "apply.submit")));
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
// Serialize enums as strings across the API for consistent client contracts
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    try
    {
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            Console.WriteLine($"Applying migrations: {string.Join(", ", pending)}");
        }
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Migration error: {ex.Message}");
        throw;
    }
}
await DemoTenantSeeder.SeedAsync(app.Services, builder.Configuration);

// Optional one-shot migrator mode for docker-compose init
var migrateOnly = Environment.GetEnvironmentVariable("MIGRATE_ONLY");
if (string.Equals(migrateOnly, "true", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("MIGRATE_ONLY=true: migrations and seed completed; exiting.");
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapPost("/debug/tenants/{slug}/rotate-consent-key", async (
        string slug,
        IConsentKeyRotator rotator,
        IAuditEventSink audit,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var key = await rotator.RotateAsync(slug, cancellationToken);
            await audit.EmitAsync(new AuditEventDescriptor(
                Category: "keys",
                Action: "rotation",
                EntityType: nameof(TenantKey),
                EntityId: key.KeyId,
                Tenant: slug,
                CreatedAt: DateTime.UtcNow));
            return Results.Ok(new
            {
                tenant = slug,
                keyId = key.KeyId,
                status = key.Status.ToString(),
                activatedAt = key.ActivatedAt,
                expiresAt = key.ExpiresAt
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Debug rotate failed for tenant {Tenant}", slug);
            return Results.NotFound(new { error = "tenant_not_found" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = ex.Message });
        }
    }).WithTags("Debug")
      .WithOpenApi(op =>
      {
          op.Summary = "DEV ONLY: Force consent signing key rotation";
          op.Description = "For development and testing, rotates the consent signing key for the given tenant.";
          return op;
      });
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
            .SelectMany(entry => ApiHelpers.ProjectKeys(entry.Value, entry.Key))
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

    var keys = ApiHelpers.ProjectKeys(jwks, slug).ToArray();
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

    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
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

    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
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


app.MapPost("/v1/consent-requests", async (
    ClaimsPrincipal user,
    [FromBody] CreateConsentRequestDto dto,
    GatewayDbContext db,
    IClientSecretHasher hasher,
    ILogger<Program> logger) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
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

    var code = ApiHelpers.GenerateVerificationCode();
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

app.MapGet("/v1/consent-requests", async (
    ClaimsPrincipal user,
    [FromQuery] string? email,
    [FromQuery] string? status,
    [FromQuery] int? take,
    GatewayDbContext db) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
    {
        return Results.Forbid();
    }

    var query = db.ConsentRequests.AsNoTracking()
        .Where(r => r.AgentTenantId == tenantSlug);
    if (!string.IsNullOrWhiteSpace(email))
    {
        var em = email.Trim().ToLower();
        query = query.Where(r => r.CandidateEmail == em);
    }
    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ConsentRequestStatus>(status, true, out var parsed))
    {
        query = query.Where(r => r.Status == parsed);
    }
    var size = Math.Clamp(take ?? 50, 1, 200);
    var rows = await query
        .OrderByDescending(r => r.CreatedAt)
        .Take(size)
        .Select(r => new
        {
            r.Id,
            r.CandidateEmail,
            r.Status,
            r.CreatedAt,
            r.ExpiresAt,
            r.DecisionAt,
            r.VerifiedAt,
            consent_id = r.ConsentId,
            link = $"/consent/{r.Id}"
        })
        .ToListAsync();
    return Results.Ok(rows);
}).RequireAuthorization("apply.submit");

app.MapPost("/v1/consent-requests/{id:guid}/cancel", async (
    ClaimsPrincipal user,
    Guid id,
    GatewayDbContext db,
    IAuditEventSink audit,
    HttpContext http) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
    {
        return Results.Forbid();
    }

    var request = await db.ConsentRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (request is null)
    {
        return Results.NotFound();
    }
    if (!string.Equals(request.AgentTenantId, tenantSlug, StringComparison.Ordinal))
    {
        return Results.Forbid();
    }
    if (request.Status is ConsentRequestStatus.Approved or ConsentRequestStatus.Denied or ConsentRequestStatus.Expired)
    {
        return Results.BadRequest(new { error = "cannot_cancel" });
    }

    request.Status = ConsentRequestStatus.Denied; // interpret as agent-cancelled in demo
    request.DecisionAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var corr = http.Request.Headers.TryGetValue("X-Correlation-ID", out var rcid) ? rcid.ToString() : http.TraceIdentifier;
    await audit.EmitAsync(new AuditEventDescriptor(
        Category: "consent_request",
        Action: "cancelled",
        EntityType: nameof(ConsentRequest),
        EntityId: request.Id.ToString(),
        Tenant: request.AgentTenantId,
        CreatedAt: DateTime.UtcNow,
        Metadata: $"cid={corr}"));

    return Results.NoContent();
}).RequireAuthorization("apply.submit")
  .WithTags("Consents");

app.MapGet("/v1/consents", async (
    ClaimsPrincipal user,
    [FromQuery] int? take,
    GatewayDbContext db) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
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

    return Results.Ok(consents.Select(ApiHelpers.MapConsent));
}).RequireAuthorization("apply.submit");

app.MapGet("/v1/consents/{id:guid}", async (
    ClaimsPrincipal user,
    Guid id,
    GatewayDbContext db) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
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

    return Results.Ok(ApiHelpers.MapConsent(consent));
}).RequireAuthorization("apply.submit");

app.MapGet("/v1/applications", async (
    ClaimsPrincipal user,
    [FromQuery] string? email,
    [FromQuery] int? take,
    GatewayDbContext db) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
    {
        return Results.Forbid();
    }
    var size = Math.Clamp(take ?? 50, 1, 200);
    var query = db.Applications.AsNoTracking()
        .Where(a => a.AgentTenantId == tenantSlug)
        .OrderByDescending(a => a.SubmittedAt)
        .Take(size);
    if (!string.IsNullOrWhiteSpace(email))
    {
        var norm = email.Trim().ToLowerInvariant();
        query = db.Applications.AsNoTracking()
            .Where(a => a.AgentTenantId == tenantSlug && db.Consents.Any(c => c.Id == a.ConsentId && c.ApprovedByEmail == norm))
            .OrderByDescending(a => a.SubmittedAt)
            .Take(size);
    }
    var rows = await query.ToListAsync();
    return Results.Ok(rows.Select(ApiHelpers.MapApplication));
}).RequireAuthorization("apply.submit")
  .WithTags("Applications")
  .WithOpenApi(op =>
  {
      op.Summary = "List applications (optional filter by candidate email)";
      return op;
  });

app.MapPost("/v1/applications", async (
    [FromHeader(Name = "X-JWS-Signature")] string? jws,
    [FromBody] ApplyPayloadDto payload,
    GatewayDbContext db,
    IHttpClientFactory httpFactory,
    IJwsVerifier verifier,
    IOptions<ConsentTokenOptions> consentTokenOptions,
    IOptions<ConsentLifecycleOptions> lifecycleOptions,
    HttpContext http,
    IAuditEventSink audit,
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

    if (string.IsNullOrWhiteSpace(payload.ConsentToken))
    {
        return Results.Unauthorized();
    }

    Consent? consent;
    Guid tokenId;

    if (payload.ConsentToken.StartsWith("ctok:", StringComparison.Ordinal))
    {
        var parts = payload.ConsentToken.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out tokenId))
        {
            return Results.Unauthorized();
        }

        consent = await db.Consents.Include(c => c.Candidate)
            .FirstOrDefaultAsync(c => c.TokenId == tokenId);
        if (consent is null || consent.Status != ConsentStatus.Active || consent.ExpiresAt <= DateTime.UtcNow)
        {
            return Results.Forbid();
        }

        var nowC = DateTime.UtcNow;
        var withinGraceC = nowC <= consent.TokenExpiresAt.AddDays(Math.Max(0, lifecycleOptions.Value.ExpiryGraceDays));
        if (consent.TokenExpiresAt <= nowC && !withinGraceC)
        {
            return Results.Forbid();
        }
        if (consent.TokenExpiresAt <= nowC && withinGraceC)
        {
            logger.LogInformation("Accepting consent token within grace window for consent {ConsentId}", consent.Id);
            await audit.EmitAsync(new AuditEventDescriptor(
                Category: "application",
                Action: "token_grace_accept",
                EntityType: nameof(Application),
                EntityId: "pending",
                Tenant: consent.AgentTenantId,
                CreatedAt: DateTime.UtcNow,
                Jti: consent.TokenId.ToString()));
            GatewayMetrics.AppTokenGraceAccepted.Add(1, new KeyValuePair<string, object?>("tenant", consent.AgentTenantId));
        }
    }
    else
    {
        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwtToken;
        try
        {
            jwtToken = handler.ReadJwtToken(payload.ConsentToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse consent token JWT.");
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(jwtToken.Id, out tokenId))
        {
            return Results.Unauthorized();
        }

        consent = await db.Consents.Include(c => c.Candidate)
            .FirstOrDefaultAsync(c => c.TokenId == tokenId);
        if (consent is null || consent.Status != ConsentStatus.Active || consent.ExpiresAt <= DateTime.UtcNow)
        {
            return Results.Forbid();
        }

        var agentTenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == consent.AgentTenantId);
        if (agentTenant is null)
        {
            return Results.Forbid();
        }

        var keyRecord = await db.TenantKeys.AsNoTracking()
            .FirstOrDefaultAsync(k =>
                k.TenantId == agentTenant.Id &&
                k.KeyId == jwtToken.Header.Kid &&
                k.Purpose == TenantKeyPurpose.ConsentToken &&
                k.Status != TenantKeyStatus.Retired);
        if (keyRecord is null)
        {
            logger.LogWarning("No active consent signing key found for tenant {Tenant} and kid {Kid}.", consent.AgentTenantId, jwtToken.Header.Kid);
            return Results.Unauthorized();
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = consentTokenOptions.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = consent.BoardTenantId,
            ValidateLifetime = false, // we will enforce lifetime + grace manually
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new JsonWebKey(keyRecord.PublicJwk),
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(payload.ConsentToken, validationParameters, out _);
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Consent token validation failed for consent {ConsentId}", consent.Id);
            return Results.Unauthorized();
        }

        if (!ValidateConsentClaims(principal, consent))
        {
            logger.LogWarning("Consent token claims mismatch for consent {ConsentId}", consent.Id);
            return Results.Forbid();
        }

        // Enforce token lifetime with grace window
        var now = DateTime.UtcNow;
        var tokenExp = jwtToken.ValidTo.ToUniversalTime();
        var withinGrace = now <= tokenExp.AddDays(Math.Max(0, lifecycleOptions.Value.ExpiryGraceDays));
        if (tokenExp <= now && !withinGrace)
        {
            logger.LogWarning("Consent token expired beyond grace for consent {ConsentId}", consent.Id);
            await audit.EmitAsync(new AuditEventDescriptor(
                Category: "application",
                Action: "token_grace_reject",
                EntityType: nameof(Application),
                EntityId: "pending",
                Tenant: consent.AgentTenantId,
                CreatedAt: DateTime.UtcNow,
                Jti: consent.TokenId.ToString()));
            GatewayMetrics.AppTokenGraceRejected.Add(1, new KeyValuePair<string, object?>("tenant", consent.AgentTenantId));
            return Results.Forbid();
        }
        if (tokenExp <= now && withinGrace)
        {
            logger.LogInformation("Accepting consent token within grace window for consent {ConsentId}", consent.Id);
            await audit.EmitAsync(new AuditEventDescriptor(
                Category: "application",
                Action: "token_grace_accept",
                EntityType: nameof(Application),
                EntityId: "pending",
                Tenant: consent.AgentTenantId,
                CreatedAt: DateTime.UtcNow,
                Jti: consent.TokenId.ToString()));
            GatewayMetrics.AppTokenGraceAccepted.Add(1, new KeyValuePair<string, object?>("tenant", consent.AgentTenantId));
        }

        if (consent.TokenHash is not null)
        {
            var computed = ComputeConsentTokenHash(payload.ConsentToken);
            if (!string.Equals(computed, consent.TokenHash, StringComparison.Ordinal))
            {
                logger.LogWarning("Consent token hash mismatch for consent {ConsentId}", consent.Id);
                return Results.Forbid();
            }
        }
    }

    var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, payloadSerializerOptions);
    if (!JwsHelpers.TryParseDetachedJws(jws, payloadBytes, out var submissionHeader))
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
    var corr = http.Request.Headers.TryGetValue("X-Correlation-ID", out var cid) ? cid.ToString() : http.TraceIdentifier;
    await audit.EmitAsync(new AuditEventDescriptor(
        Category: "application",
        Action: "created",
        EntityType: nameof(Application),
        EntityId: appRec.Id.ToString(),
        Tenant: appRec.AgentTenantId,
        CreatedAt: DateTime.UtcNow,
        Metadata: $"cid={corr}"));

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
            await audit.EmitAsync(new AuditEventDescriptor(
                Category: "application",
                Action: "receipt_missing",
                EntityType: nameof(Application),
                EntityId: appRec.Id.ToString(),
                Tenant: appRec.AgentTenantId,
                CreatedAt: DateTime.UtcNow,
                Metadata: $"cid={corr}"));
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
                await audit.EmitAsync(new AuditEventDescriptor(
                    Category: "application",
                    Action: "accepted",
                    EntityType: nameof(Application),
                    EntityId: appRec.Id.ToString(),
                    Tenant: appRec.AgentTenantId,
                    CreatedAt: DateTime.UtcNow,
                    Metadata: $"cid={corr}"));
                await audit.EmitAsync(new AuditEventDescriptor(
                    Category: "receipt",
                    Action: "verified",
                    EntityType: nameof(Application),
                    EntityId: appRec.Id.ToString(),
                    Tenant: appRec.AgentTenantId,
                    CreatedAt: DateTime.UtcNow,
                    Metadata: $"cid={corr}"));
                return Results.Accepted($"/v1/applications/{appRec.Id}", new { id = appRec.Id, status = appRec.Status });
            }

            logger.LogWarning("Receipt signature validation failed for application {ApplicationId}", appRec.Id);
            await audit.EmitAsync(new AuditEventDescriptor(
                Category: "receipt",
                Action: "verification_failed",
                EntityType: nameof(Application),
                EntityId: appRec.Id.ToString(),
                Tenant: appRec.AgentTenantId,
                CreatedAt: DateTime.UtcNow,
                Metadata: $"cid={corr}"));
        }
    }

    appRec.Status = ApplicationStatus.Failed;
    await db.SaveChangesAsync();
    await audit.EmitAsync(new AuditEventDescriptor(
        Category: "application",
        Action: "failed",
        EntityType: nameof(Application),
        EntityId: appRec.Id.ToString(),
        Tenant: appRec.AgentTenantId,
        CreatedAt: DateTime.UtcNow,
        Metadata: $"cid={corr}"));
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
        : TypedResults.Ok(ApiHelpers.MapApplication(application));
}).WithTags("Applications")
  .WithOpenApi(op =>
  {
      op.Summary = "Retrieve application status and receipt metadata";
      return op;
  });

app.MapPost("/v1/consents/{id:guid}/revoke", async (Guid id, GatewayDbContext db, HttpContext http, IAuditEventSink audit) =>
{
    var consent = await db.Consents.FindAsync(id);
    if (consent is null)
    {
        return Results.NotFound();
    }

    consent.Status = ConsentStatus.Revoked;
    consent.RevokedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    var corr = http.Request.Headers.TryGetValue("X-Correlation-ID", out var rcid) ? rcid.ToString() : http.TraceIdentifier;
    await audit.EmitAsync(new AuditEventDescriptor(
        Category: "consent",
        Action: "revoked",
        EntityType: nameof(Consent),
        EntityId: consent.Id.ToString(),
        Tenant: consent.AgentTenantId,
        CreatedAt: DateTime.UtcNow,
        Metadata: $"cid={corr}"));
    return Results.NoContent();
});

app.MapPost("/v1/consents/{id:guid}/renew", async (
    ClaimsPrincipal user,
    Guid id,
    GatewayDbContext db,
    IConsentLifecycleService lifecycle) =>
{
    var (tenantSlug, tenantType) = AuthHelpers.GetTenantContext(user);
    if (tenantSlug is null || tenantType != TenantType.Agent)
    {
        return Results.Forbid();
    }

    var consent = await db.Consents.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    if (consent is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(consent.AgentTenantId, tenantSlug, StringComparison.Ordinal))
    {
        return Results.Forbid();
    }

    var result = await lifecycle.RenewAsync(id);
    return result is null
        ? Results.BadRequest(new { error = "renewal_not_allowed" })
        : Results.Ok(new
        {
            token = result.Token,
            token_id = result.TokenId,
            issued_at = result.IssuedAt,
            expires_at = result.ExpiresAt,
            kid = result.KeyId,
            alg = result.Algorithm,
        });
}).RequireAuthorization("apply.submit")
  .WithTags("Consents")
  .WithOpenApi(op =>
  {
      op.Summary = "Renew a consent's token if within policy window";
      return op;
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

app.MapPost("/internal/audit/verify", async (
    [FromQuery] string tenant,
    [FromQuery] int? days,
    IAuditVerifier verifier) =>
{
    if (string.IsNullOrWhiteSpace(tenant))
    {
        return Results.BadRequest(new { error = "tenant_required" });
    }

    var end = DateTime.UtcNow;
    var start = end.AddDays(-Math.Clamp(days ?? 1, 1, 30));
    var result = await verifier.VerifyAsync(tenant, start, end);
    return Results.Ok(result);
}).WithTags("Internal").WithOpenApi(op =>
{
    op.Summary = "Run audit integrity verification";
    return op;
});

app.MapGet("/internal/audit/status", async (
    [FromQuery] string tenant,
    [FromQuery] int? take,
    IAuditVerifier verifier) =>
{
    if (string.IsNullOrWhiteSpace(tenant))
    {
        return Results.BadRequest(new { error = "tenant_required" });
    }

    var rows = await verifier.GetRecentRunsAsync(tenant, Math.Clamp(take ?? 10, 1, 100));
    return Results.Ok(rows);
}).WithTags("Internal").WithOpenApi(op =>
{
    op.Summary = "List recent audit verification runs";
    return op;
});

app.MapRazorPages();
app.Run();

bool ValidateConsentClaims(ClaimsPrincipal principal, Consent consent)
{
    if (principal is null || consent is null)
    {
        return false;
    }

    var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
    if (!Guid.TryParse(jtiClaim, out var jti) || jti != consent.TokenId)
    {
        return false;
    }

    if (!string.Equals(principal.FindFirst("cid")?.Value, consent.Id.ToString(), StringComparison.Ordinal))
    {
        return false;
    }

    if (!string.Equals(principal.FindFirst("agent")?.Value, consent.AgentTenantId, StringComparison.Ordinal))
    {
        return false;
    }

    if (!string.Equals(principal.FindFirst("board")?.Value, consent.BoardTenantId, StringComparison.Ordinal))
    {
        return false;
    }

    if (!string.Equals(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, consent.CandidateId.ToString(), StringComparison.Ordinal))
    {
        return false;
    }

    return true;
}

string ComputeConsentTokenHash(string token)
{
    var bytes = Encoding.UTF8.GetBytes(token);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
}


 

