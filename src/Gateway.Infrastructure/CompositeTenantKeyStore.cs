using Gateway.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Infrastructure;

public sealed class CompositeTenantKeyStore : ITenantKeyStore
{
    private readonly ConfigurationTenantKeyStore _configStore;
    private readonly IServiceScopeFactory _scopeFactory;

    public CompositeTenantKeyStore(ConfigurationTenantKeyStore configStore, IServiceScopeFactory scopeFactory)
    {
        _configStore = configStore;
        _scopeFactory = scopeFactory;
    }

    public bool TryGetKeys(string tenantSlug, out JsonWebKeySet? jwks)
    {
        jwks = BuildForTenant(tenantSlug);
        return jwks?.Keys is { Count: > 0 };
    }

    public IEnumerable<KeyValuePair<string, JsonWebKeySet>> GetAll()
    {
        var map = new Dictionary<string, JsonWebKeySet>(StringComparer.Ordinal);

        foreach (var pair in _configStore.GetAll())
        {
            if (pair.Value?.Keys is null || pair.Value.Keys.Count == 0)
            {
                continue;
            }

            map[pair.Key] = CloneSet(pair.Value);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var records = db.TenantKeys.AsNoTracking()
            .Where(k => k.Purpose == TenantKeyPurpose.ConsentToken && k.Status != TenantKeyStatus.Retired)
            .Join(db.Tenants.AsNoTracking(),
                k => k.TenantId,
                t => t.Id,
                (k, t) => new { t.Slug, Key = k })
            .ToList();

        foreach (var record in records)
        {
            if (!map.TryGetValue(record.Slug, out var set))
            {
                set = new JsonWebKeySet();
                map[record.Slug] = set;
            }

            TryAddKey(set, record.Key.PublicJwk);
        }

        return map.Select(kvp => new KeyValuePair<string, JsonWebKeySet>(kvp.Key, kvp.Value));
    }

    private JsonWebKeySet? BuildForTenant(string tenantSlug)
    {
        var set = new JsonWebKeySet();
        if (_configStore.TryGetKeys(tenantSlug, out var configSet) && configSet?.Keys is not null)
        {
            foreach (var key in configSet.Keys)
            {
                AddKey(set, key);
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var keys = db.TenantKeys.AsNoTracking()
            .Where(k => k.Purpose == TenantKeyPurpose.ConsentToken &&
                        k.Status != TenantKeyStatus.Retired &&
                        k.Tenant != null &&
                        k.Tenant.Slug == tenantSlug)
            .Select(k => k.PublicJwk)
            .ToList();

        foreach (var jwkJson in keys)
        {
            TryAddKey(set, jwkJson);
        }

        var collection = set.Keys;
        return collection is null || collection.Count == 0 ? null : set;
    }

    private static JsonWebKeySet CloneSet(JsonWebKeySet source)
    {
        if (source?.Keys is null)
        {
            return new JsonWebKeySet();
        }

        var clone = new JsonWebKeySet();
        foreach (var key in source.Keys)
        {
            AddKey(clone, key);
        }

        return clone;
    }

    private static void TryAddKey(JsonWebKeySet set, string jwkJson)
    {
        if (string.IsNullOrWhiteSpace(jwkJson))
        {
            return;
        }

        try
        {
            var jwk = new JsonWebKey(jwkJson);
            AddKey(set, jwk);
        }
        catch
        {
            // ignore malformed JWKs to avoid poisoning the cache
        }
    }

    private static void AddKey(JsonWebKeySet set, JsonWebKey key)
    {
        if (key is null)
        {
            return;
        }

        var keys = set.Keys;
        if (keys is null)
        {
            return;
        }

        keys.Add(key);
    }
}
