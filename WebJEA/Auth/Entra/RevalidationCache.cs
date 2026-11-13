using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace WebJEA.Auth.Entra;

/// <summary>
/// Each user's group IDs from their last successful pre-execute revalidation, kept for
/// Authentication:Entra:RevalidateCacheSeconds (default 300; 0 disables caching). Execute
/// skips Graph while an entry exists, and the menu and command metadata read it so they
/// match what execute allows. Keyed by tenant and object ID; users without an oid claim
/// aren't cached.
/// </summary>
public class RevalidationCache
{
    public const int DefaultCacheSeconds = 300;

    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public RevalidationCache(IMemoryCache cache, IConfiguration config)
    {
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(Math.Max(0, config.GetValue("Authentication:Entra:RevalidateCacheSeconds", DefaultCacheSeconds)));
    }

    public TimeSpan Ttl => _ttl;

    public bool TryGet(ClaimsPrincipal principal, out IReadOnlyList<string> groupIds)
    {
        groupIds = null;
        string key = KeyFor(principal);
        return key != null && _cache.TryGetValue(key, out groupIds);
    }

    public void Set(ClaimsPrincipal principal, IReadOnlyList<string> groupIds)
    {
        string key = KeyFor(principal);
        if (key == null || _ttl == TimeSpan.Zero) return;
        _cache.Set(key, groupIds, _ttl);
    }

    public void Remove(ClaimsPrincipal principal)
    {
        string key = KeyFor(principal);
        if (key != null) _cache.Remove(key);
    }

    private static string KeyFor(ClaimsPrincipal principal)
    {
        string oid = principal?.FindFirst("oid")?.Value
                     ?? principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(oid)) return null;

        string tid = principal.FindFirst("tid")?.Value
                     ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                     ?? "";
        return "webjea:revalidate:" + tid.ToLowerInvariant() + ":" + oid.ToLowerInvariant();
    }
}
