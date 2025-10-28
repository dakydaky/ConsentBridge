using Gateway.Domain;
using Microsoft.Extensions.Configuration;

namespace Gateway.Infrastructure;

public interface ITenantSigningSecretProvider
{
    bool TryGetSigningSecret(string tenantSlug, out string? secret);
}

public sealed class ConfigurationTenantSigningSecretProvider : ITenantSigningSecretProvider
{
    private readonly Dictionary<string, string> _secrets;

    public ConfigurationTenantSigningSecretProvider(IConfiguration configuration)
    {
        var section = configuration.GetSection("DemoTenants");
        _secrets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var child in section.GetChildren())
        {
            var slug = child.GetValue<string>("Slug");
            var secret = child.GetValue<string>("SignatureSecret");
            if (!string.IsNullOrWhiteSpace(slug) && !string.IsNullOrWhiteSpace(secret))
            {
                _secrets[slug!] = secret!;
            }
        }
    }

    public bool TryGetSigningSecret(string tenantSlug, out string? secret) =>
        _secrets.TryGetValue(tenantSlug, out secret);
}
