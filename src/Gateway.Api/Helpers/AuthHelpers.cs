using System.Security.Claims;
using Gateway.Domain;

namespace Gateway.Api;

internal static class AuthHelpers
{
    internal static (string? slug, TenantType? type) GetTenantContext(ClaimsPrincipal user)
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

    internal static bool HasScope(ClaimsPrincipal user, string scope)
    {
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

    private static IEnumerable<Claim> EnumerateScopeClaims(ClaimsPrincipal principal)
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
}

