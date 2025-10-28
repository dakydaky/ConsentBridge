using System;
using System.Linq;
using System.Threading.Tasks;
using Gateway.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure;

public static class DemoTenantSeeder
{
    private const string SectionName = "DemoTenants";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        if (!section.Exists())
        {
            return;
        }

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<GatewayDbContext>();
        var hasher = provider.GetRequiredService<IClientSecretHasher>();
        var logger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DemoTenantSeeder");

        var now = DateTime.UtcNow;

        foreach (var child in section.GetChildren())
        {
            var cfg = child.Get<DemoTenantConfig>();
            if (cfg is null)
            {
                logger.LogWarning("Skipping demo tenant configuration for section {Section} due to binding failure.", child.Path);
                continue;
            }

            var tenant = await db.Tenants
                .Include(t => t.Credentials)
                .FirstOrDefaultAsync(t => t.Slug == cfg.Slug);

            if (tenant is null)
            {
                tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Slug = cfg.Slug,
                    DisplayName = cfg.DisplayName ?? cfg.Slug,
                    Type = cfg.Type ?? InferTypeFromKey(child.Key),
                    JwksEndpoint = cfg.JwksEndpoint,
                    CallbackUrl = cfg.CallbackUrl,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Tenants.Add(tenant);
                logger.LogInformation("Seeded demo tenant {Slug}", tenant.Slug);
            }
            else
            {
                tenant.DisplayName = cfg.DisplayName ?? tenant.DisplayName;
                tenant.Type = cfg.Type ?? tenant.Type;
                tenant.JwksEndpoint = cfg.JwksEndpoint;
                tenant.CallbackUrl = cfg.CallbackUrl;
                tenant.IsActive = true;
                tenant.UpdatedAt = now;
            }

            var clientId = cfg.ClientId ?? $"{cfg.Slug}_client";
            var scopes = string.Join(' ', cfg.Scopes?.Where(s => !string.IsNullOrWhiteSpace(s)) ?? new[] { "apply.submit" });
            var secret = cfg.ClientSecret;

            if (!string.IsNullOrWhiteSpace(secret))
            {
                var credential = tenant.Credentials.FirstOrDefault(c => c.ClientId == clientId);
                var hash = hasher.HashSecret(secret);

                if (credential is null)
                {
                    credential = new TenantCredential
                    {
                        Id = Guid.NewGuid(),
                        Tenant = tenant,
                        TenantId = tenant.Id,
                        ClientId = clientId,
                        ClientSecretHash = hash,
                        Scopes = scopes,
                        CreatedAt = now,
                        LastRotatedAt = now,
                        IsActive = true
                    };
                    tenant.Credentials.Add(credential);
                    logger.LogInformation("Seeded credential for tenant {Slug}", tenant.Slug);
                }
                else if (!hasher.Verify(secret, credential.ClientSecretHash))
                {
                    credential.ClientSecretHash = hash;
                    credential.Scopes = scopes;
                    credential.IsActive = true;
                    credential.LastRotatedAt = now;
                    logger.LogInformation("Rotated credential for tenant {Slug}", tenant.Slug);
                }
                else
                {
                    credential.Scopes = scopes;
                    credential.IsActive = true;
                }
            }
            else
            {
                logger.LogWarning("Demo tenant {Slug} missing ClientSecret; skipping credential seed.", cfg.Slug);
            }
        }

        await db.SaveChangesAsync();
    }

    private static TenantType InferTypeFromKey(string key)
    {
        if (key.Equals("agent", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("agent", StringComparison.OrdinalIgnoreCase))
        {
            return TenantType.Agent;
        }

        return TenantType.Board;
    }

    private sealed class DemoTenantConfig
    {
        public string Slug { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public TenantType? Type { get; set; }
        public string? JwksEndpoint { get; set; }
        public string? CallbackUrl { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string[]? Scopes { get; set; }
        public string? SignatureSecret { get; set; }
    }
}
