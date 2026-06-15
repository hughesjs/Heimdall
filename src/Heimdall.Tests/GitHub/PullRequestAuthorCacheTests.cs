using Heimdall.Core.GitHub;
using Shouldly;

namespace Heimdall.Tests.GitHub;

public class PullRequestAuthorCacheTests
{
    [Fact]
    public async Task Resolves_via_the_fetch_delegate_on_a_miss()
    {
        var cache = new PullRequestAuthorCache((_, _, _, _) => Task.FromResult<string?>("alice"));

        (await cache.GetAsync("octo", "demo", 7, default)).ShouldBe("alice");
    }

    [Fact]
    public async Task Caches_so_each_pr_is_fetched_at_most_once()
    {
        var calls = 0;
        var cache = new PullRequestAuthorCache((_, _, _, _) =>
        {
            calls++;
            return Task.FromResult<string?>("alice");
        });

        await cache.GetAsync("octo", "demo", 7, default);
        await cache.GetAsync("octo", "demo", 7, default);
        await cache.GetAsync("octo", "demo", 7, default);

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Different_prs_are_fetched_separately()
    {
        var calls = 0;
        var cache = new PullRequestAuthorCache((_, _, number, _) =>
        {
            calls++;
            return Task.FromResult<string?>($"author-{number}");
        });

        (await cache.GetAsync("octo", "demo", 1, default)).ShouldBe("author-1");
        (await cache.GetAsync("octo", "demo", 2, default)).ShouldBe("author-2");

        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Null_results_are_not_cached_so_a_later_resolution_can_succeed()
    {
        var results = new Queue<string?>([null, "alice"]);
        var calls = 0;
        var cache = new PullRequestAuthorCache((_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(results.Dequeue());
        });

        (await cache.GetAsync("octo", "demo", 7, default)).ShouldBeNull();
        (await cache.GetAsync("octo", "demo", 7, default)).ShouldBe("alice");
        calls.ShouldBe(2);
    }
}
