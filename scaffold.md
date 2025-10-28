Below is a complete repo scaffold you can paste into a new folder. It includes:

* **Gateway.Api** (Minimal APIs + EF Core + Npgsql)
* **Gateway.Domain** (entities, DTOs)
* **Gateway.Infrastructure** (DbContext, migrations-on-start)
* **MockBoard.Adapter** (fake EU job board accepting signed applies)
* **docker-compose.yml** (API + Postgres + MockBoard)
* **Makefile** helper targets

> Notes:
>
> * Targets **.NET 9** (LTS).
> * For demo, **crypto verification is stubbed** to keep first run simple; the interface is ready for a real JWS verifier later.
> * EF Core applies migrations automatically on boot.

---

```text
consent-apply-gateway/
├─ src/
│  ├─ Gateway.Api/
│  │  ├─ Program.cs
│  │  ├─ Gateway.Api.csproj
│  │  ├─ appsettings.json
│  │  ├─ appsettings.Development.json
│  │  └─ Dockerfile
│  ├─ Gateway.Domain/
│  │  ├─ Entities.cs
│  │  ├─ Dtos.cs
│  │  ├─ Services.cs
│  │  └─ Gateway.Domain.csproj
│  ├─ Gateway.Infrastructure/
│  │  ├─ GatewayDbContext.cs
│  │  ├─ DesignTimeFactory.cs
│  │  ├─ InfrastructureServices.cs
│  │  ├─ Migrations/ (auto-created on first `dotnet ef migrations add`)
│  │  └─ Gateway.Infrastructure.csproj
│  └─ MockBoard.Adapter/
│     ├─ Program.cs
│     ├─ MockBoard.Adapter.csproj
│     └─ Dockerfile
├─ docker-compose.yml
├─ Makefile
├─ README.md
└─ global.json
```

---

### `global.json`

```json
{
  "sdk": {
    "version": "8.0.400",
    "rollForward": "latestFeature"
  }
}
```

---

### `src/Gateway.Api/Gateway.Api.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Gateway.Domain/Gateway.Domain.csproj" />
    <ProjectReference Include="../Gateway.Infrastructure/Gateway.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.7">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.1" />
    <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
  </ItemGroup>
</Project>
```

---

### `src/Gateway.Api/appsettings.json`

```json
{
  "ConnectionStrings": {
    "Default": "Host=postgres;Port=5432;Database=gateway;Username=gateway;Password=devpass"
  },
  "AllowedHosts": "*",
  "Serilog": {
    "MinimumLevel": "Information"
  }
}
```

### `src/Gateway.Api/appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=gateway;Username=gateway;Password=devpass"
  }
}
```

---

### `src/Gateway.Api/Program.cs`

```csharp
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Db
builder.Services.AddDbContext<GatewayDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Infra services
builder.Services.AddGatewayInfrastructure();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Auto-migrate for demo purposes
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

// Create Consent (simplified for demo)
app.MapPost("/v1/consents", async ([FromBody] CreateConsentDto dto, GatewayDbContext db) =>
{
    var cand = await db.Candidates.FirstOrDefaultAsync(c => c.EmailHash == dto.CandidateEmail.ToLowerInvariant());
    if (cand == null)
    {
        cand = new Candidate { Id = Guid.NewGuid(), EmailHash = dto.CandidateEmail.ToLowerInvariant(), CreatedAt = DateTime.UtcNow };
        db.Candidates.Add(cand);
    }

    var consent = new Consent
    {
        Id = Guid.NewGuid(),
        CandidateId = cand.Id,
        AgentTenantId = dto.AgentTenantId,
        BoardTenantId = dto.BoardTenantId,
        Status = ConsentStatus.Active,
        IssuedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMonths(6),
        Scopes = "apply:submit"
    };
    db.Consents.Add(consent);
    await db.SaveChangesAsync();

    // Return a fake JWT-like token for demo
    var token = $"ctok:{consent.Id}";
    return Results.Ok(new { consent_token = token, consent_id = consent.Id });
});

// Submit Application (signature & consent validation are stubbed for demo)
app.MapPost("/v1/applications", async (
    HttpRequest req,
    [FromHeader(Name = "X-JWS-Signature")] string? jws,
    [FromBody] ApplyPayloadDto payload,
    GatewayDbContext db,
    IHttpClientFactory httpFactory) =>
{
    if (payload is null) return Results.BadRequest();

    // Validate consent token
    if (string.IsNullOrWhiteSpace(payload.ConsentToken) || !payload.ConsentToken.StartsWith("ctok:"))
        return Results.Unauthorized();

    var consentId = Guid.Parse(payload.ConsentToken.Split(':')[1]);
    var consent = await db.Consents.Include(c => c.Candidate).FirstOrDefaultAsync(c => c.Id == consentId);
    if (consent is null || consent.Status != ConsentStatus.Active || consent.ExpiresAt <= DateTime.UtcNow)
        return Results.Forbid();

    // Persist application
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

    // Forward to MockBoard
    var client = httpFactory.CreateClient("mockboard");
    var resp = await client.PostAsJsonAsync("/v1/mock/applications", new
    {
        application_id = appRec.Id,
        job_external_id = payload.Job.ExternalId,
        candidate_id = consent.CandidateId,
        payload = payload
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

// Get application
app.MapGet("/v1/applications/{id:guid}", async (Guid id, GatewayDbContext db) =>
{
    var appRec = await db.Applications.FindAsync(id);
    return appRec is null ? Results.NotFound() : Results.Ok(appRec);
});

// Revoke
app.MapPost("/v1/consents/{id:guid}/revoke", async (Guid id, GatewayDbContext db) =>
{
    var consent = await db.Consents.FindAsync(id);
    if (consent is null) return Results.NotFound();
    consent.Status = ConsentStatus.Revoked;
    consent.RevokedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// HttpClient for MockBoard
builder.Services.AddHttpClient("mockboard", c =>
{
    // In docker-compose the service name is mockboard
    c.BaseAddress = new Uri(Environment.GetEnvironmentVariable("MOCKBOARD_URL") ?? "http://mockboard:8081");
});

app.Run();
```

---

### `src/Gateway.Domain/Gateway.Domain.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

### `src/Gateway.Domain/Entities.cs`

```csharp
namespace Gateway.Domain;

public class Candidate
{
    public Guid Id { get; set; }
    public string EmailHash { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}

public class Consent
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public string AgentTenantId { get; set; } = default!;
    public string BoardTenantId { get; set; } = default!;
    public string Scopes { get; set; } = "apply:submit";
    public ConsentStatus Status { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public enum ConsentStatus { Active = 1, Revoked = 2 }

public class Application
{
    public Guid Id { get; set; }
    public Guid ConsentId { get; set; }
    public Consent? Consent { get; set; }
    public string AgentTenantId { get; set; } = default!;
    public string BoardTenantId { get; set; } = default!;
    public ApplicationStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? Receipt { get; set; }
    public string PayloadHash { get; set; } = default!;
}

public enum ApplicationStatus { Pending = 1, Accepted = 2, Failed = 3 }
```

### `src/Gateway.Domain/Dtos.cs`

```csharp
namespace Gateway.Domain;

public record CreateConsentDto(string CandidateEmail, string AgentTenantId, string BoardTenantId);

public record ApplyPayloadDto(
    string ConsentToken,
    CandidateDto Candidate,
    JobRefDto Job,
    MaterialsDto Materials,
    MetaDto Meta
);

public record CandidateDto(string Id, ContactDto Contact, PiiDto Pii, DocumentDto Cv);
public record ContactDto(string Email, string? Phone);
public record PiiDto(string FirstName, string LastName);
public record DocumentDto(string Url, string Sha256);

public record JobRefDto(string ExternalId, string Title, string Company, string ApplyEndpoint);

public record MaterialsDto(CoverLetterDto CoverLetter, IReadOnlyList<AnswerDto> Answers);
public record CoverLetterDto(string Text);
public record AnswerDto(string QuestionId, string AnswerText);

public record MetaDto(string Locale, string UserAgent, DateTime Ts);
```

### `src/Gateway.Domain/Services.cs`

```csharp
namespace Gateway.Domain;

public interface IJwsVerifier
{
    bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid);
}

public sealed class AcceptAllVerifier : IJwsVerifier
{
    public bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid) => true; // demo only
}
```

---

### `src/Gateway.Infrastructure/Gateway.Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Gateway.Domain/Gateway.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.7" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.4" />
  </ItemGroup>
</Project>
```

### `src/Gateway.Infrastructure/GatewayDbContext.cs`

```csharp
using Gateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure;

public class GatewayDbContext : DbContext
{
    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options) { }

    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<Application> Applications => Set<Application>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("uuid-ossp", schema: null, database: null);
        b.Entity<Candidate>().HasIndex(x => x.EmailHash).IsUnique();
        b.Entity<Consent>().HasOne(c => c.Candidate).WithMany().HasForeignKey(c => c.CandidateId);
        b.Entity<Application>().HasOne(a => a.Consent).WithMany().HasForeignKey(a => a.ConsentId);
        base.OnModelCreating(b);
    }
}
```

### `src/Gateway.Infrastructure/DesignTimeFactory.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gateway.Infrastructure;

public class DesignTimeFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=gateway;Username=gateway;Password=devpass")
            .Options;
        return new GatewayDbContext(opts);
    }
}
```

### `src/Gateway.Infrastructure/InfrastructureServices.cs`

```csharp
using Gateway.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IJwsVerifier, AcceptAllVerifier>();
        return services;
    }
}
```

---

### `src/Gateway.Api/Dockerfile`

```dockerfile
# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../../global.json ./
COPY ../../src/Gateway.Domain/Gateway.Domain.csproj src/Gateway.Domain/
COPY ../../src/Gateway.Infrastructure/Gateway.Infrastructure.csproj src/Gateway.Infrastructure/
COPY ../../src/Gateway.Api/Gateway.Api.csproj src/Gateway.Api/
RUN dotnet restore src/Gateway.Api/Gateway.Api.csproj
COPY ../../src/ ./src/
RUN dotnet publish src/Gateway.Api/Gateway.Api.csproj -c Release -o /app

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Gateway.Api.dll"]
```

---

### `src/MockBoard.Adapter/MockBoard.Adapter.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.1" />
    <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
  </ItemGroup>
</Project>
```

### `src/MockBoard.Adapter/Program.cs`

```csharp
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "mockboard-ok" }));

// Accept an application and return a receipt
app.MapPost("/v1/mock/applications", async (HttpRequest req) =>
{
    var body = await new StreamReader(req.Body).ReadToEndAsync();
    // In real life you'd verify signature & consent; here we return a fake JWS receipt
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
```

### `src/MockBoard.Adapter/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../../src/MockBoard.Adapter/MockBoard.Adapter.csproj ./
RUN dotnet restore
COPY ../../src/MockBoard.Adapter/ .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MockBoard.Adapter.dll"]
```

---

### `docker-compose.yml`

```yaml
version: "3.9"
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: gateway
      POSTGRES_PASSWORD: devpass
      POSTGRES_DB: gateway
    ports: ["5432:5432"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U gateway"]
      interval: 5s
      timeout: 3s
      retries: 20

  mockboard:
    build: ./src/MockBoard.Adapter
    ports: ["8081:8081"]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8081/health"]
      interval: 5s
      timeout: 3s
      retries: 20

  gateway:
    build: ./src/Gateway.Api
    depends_on:
      postgres:
        condition: service_healthy
      mockboard:
        condition: service_healthy
    environment:
      ConnectionStrings__Default: Host=postgres;Port=5432;Database=gateway;Username=gateway;Password=devpass
      ASPNETCORE_ENVIRONMENT: Development
      MOCKBOARD_URL: http://mockboard:8081
    ports: ["8080:8080"]
```

---

### `Makefile`

```makefile
.PHONY: up down logs seed migrate add-migration

up:
	docker compose up --build -d

logs:
	docker compose logs -f gateway mockboard postgres

down:
	docker compose down -v

add-migration:
	dotnet tool restore || true
	dotnet ef migrations add Initial --project src/Gateway.Infrastructure --startup-project src/Gateway.Api

migrate:
	dotnet ef database update --project src/Gateway.Infrastructure --startup-project src/Gateway.Api
```

---

### `README.md`

````markdown
# Consent-First Apply Gateway (Demo Scaffold)

A minimal, runnable scaffold for the EU consented apply gateway, plus a Mock Board adapter.

## Prereqs
- Docker + Docker Compose
- .NET 8 SDK (optional if you only use Docker)

## Run it
```bash
make up
# or: docker compose up --build
````

* Gateway API: [http://localhost:8080/swagger](http://localhost:8080/swagger)
* MockBoard: [http://localhost:8081/health](http://localhost:8081/health)

## Quick test (with `curl`)

1. Create a consent:

```bash
curl -s -X POST http://localhost:8080/v1/consents \
  -H 'Content-Type: application/json' \
  -d '{"CandidateEmail":"alice@example.com","AgentTenantId":"agent_acme","BoardTenantId":"mockboard_eu"}' | jq
```

Grab `consent_token` from the response (e.g., `ctok:...`).

2. Submit an application:

```bash
TOKEN=ctok:REPLACE_WITH_YOURS
curl -s -X POST http://localhost:8080/v1/applications \
  -H 'Content-Type: application/json' \
  -H 'X-JWS-Signature: demo.signature' \
  -d '{
    "ConsentToken": "'"$TOKEN"'",
    "Candidate": {
      "Id": "cand_123",
      "Contact": {"Email": "alice@example.com", "Phone": "+45 1234"},
      "Pii": {"FirstName": "Alice", "LastName": "Larsen"},
      "Cv": {"Url": "https://example/cv.pdf", "Sha256": "deadbeef"}
    },
    "Job": {
      "ExternalId": "mock:98765",
      "Title": "Backend Engineer",
      "Company": "ACME GmbH",
      "ApplyEndpoint": "quick-apply"
    },
    "Materials": {
      "CoverLetter": {"Text": "Hello!"},
      "Answers": [{"QuestionId": "q_legal_work", "AnswerText": "Yes"}]
    },
    "Meta": {"Locale": "de-DE", "UserAgent": "agent/0.1", "Ts": "2025-10-27T10:15:00Z"}
  }' | jq
```

You should see `202 Accepted` and status `Accepted`. The gateway forwards to the MockBoard and stores a receipt.

3. Revoke the consent:

```bash
CONSENT_ID=REPLACE
curl -X POST http://localhost:8080/v1/consents/$CONSENT_ID/revoke -i
```

## Local development (optional)

* Use `dotnet watch run --project src/Gateway.Api` for hot reload.
* Create migrations: `make add-migration` then `make migrate`.

## Next steps

* Swap `AcceptAllVerifier` for a real JWS verifier (ES256/EdDSA).
* Add per-tenant auth (client credentials) + JWKS endpoints.
* Implement signed receipts in MockBoard and verification in Gateway.
* Add DSR endpoints and a simple candidate portal.

```
```
