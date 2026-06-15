using Heimdall.Core.Models;

namespace Heimdall.Core.Polling;

/// <summary>Decides when to back off polling to protect the API quota.</summary>
public static class RateLimitPolicy
{
    /// <summary>
    /// Back off once the remaining quota drops below a floor that scales with the number of repos
    /// (each cycle costs roughly one call per repo, plus PR lookups), never less than 100.
    /// </summary>
    public static bool ShouldBackOff(RateLimitInfo? rateLimit, int repoCount)
    {
        if (rateLimit is null)
            return false;

        var floor = Math.Max(100, 3 * repoCount);
        return rateLimit.Remaining < floor;
    }
}
