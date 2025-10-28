using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Infrastructure;

public interface ITenantKeyStore
{
    bool TryGetKeys(string tenantSlug, out JsonWebKeySet? jwks);
}

public sealed class ConfigurationTenantKeyStore : ITenantKeyStore
{
    private readonly ConcurrentDictionary<string, JsonWebKeySet> _cache = new(StringComparer.Ordinal);

    public ConfigurationTenantKeyStore(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var section = configuration.GetSection("DemoTenants");
        foreach (var child in section.GetChildren())
        {
            var cfg = child.Get<DemoTenantKeyConfig>();
            if (cfg is null)
            {
                continue;
            }

            var slug = string.IsNullOrWhiteSpace(cfg.Slug) ? child.GetValue<string>("Slug") : cfg.Slug;
            slug ??= child.Key;

            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(cfg.JwksPath))
            {
                continue;
            }

            var absolutePath = Path.IsPathRooted(cfg.JwksPath)
                ? cfg.JwksPath
                : Path.Combine(hostEnvironment.ContentRootPath, cfg.JwksPath);

            if (!File.Exists(absolutePath))
            {
                continue;
            }

            var json = File.ReadAllText(absolutePath);
            try
            {
                var jwks = new JsonWebKeySet(json);
                _cache[slug] = jwks;
            }
            catch
            {
                // ignore invalid JWKS for demo configuration
            }
        }
    }

    public bool TryGetKeys(string tenantSlug, out JsonWebKeySet? jwks) =>
        _cache.TryGetValue(tenantSlug, out jwks);

    private sealed class DemoTenantKeyConfig
    {
        public string Slug { get; set; } = string.Empty;
        public string? JwksPath { get; set; }
    }
}
