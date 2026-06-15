using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Shouldly;

namespace Heimdall.Tests.Polling;

public class RateLimitPolicyTests
{
    private static RateLimitInfo Limit(int remaining) => new(remaining, 5000, DateTimeOffset.UtcNow.AddMinutes(30));

    [Fact]
    public void No_rate_limit_info_means_no_backoff()
    {
        RateLimitPolicy.ShouldBackOff(null, repoCount: 10).ShouldBeFalse();
    }

    [Fact]
    public void Ample_remaining_quota_means_no_backoff()
    {
        RateLimitPolicy.ShouldBackOff(Limit(4000), repoCount: 5).ShouldBeFalse();
    }

    [Fact]
    public void Backs_off_below_the_minimum_floor_of_100()
    {
        RateLimitPolicy.ShouldBackOff(Limit(99), repoCount: 1).ShouldBeTrue();
        RateLimitPolicy.ShouldBackOff(Limit(100), repoCount: 1).ShouldBeFalse();
    }

    [Fact]
    public void Floor_scales_with_repo_count()
    {
        // 50 repos -> floor of 150; 120 remaining is below it.
        RateLimitPolicy.ShouldBackOff(Limit(120), repoCount: 50).ShouldBeTrue();
        RateLimitPolicy.ShouldBackOff(Limit(200), repoCount: 50).ShouldBeFalse();
    }
}
