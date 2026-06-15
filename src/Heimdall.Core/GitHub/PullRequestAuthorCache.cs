using System.Collections.Concurrent;

namespace Heimdall.Core.GitHub;

/// <summary>
/// Resolves a PR's author login, caching permanently by (owner, repo, number). PR authors are
/// immutable, so each PR is fetched at most once — bounded by the number of PRs ever seen, not by
/// poll frequency. The fetch delegate is injected so the cache is testable without GitHub.
/// </summary>
public sealed class PullRequestAuthorCache
{
    private readonly Func<string, string, int, CancellationToken, Task<string?>> _fetch;
    private readonly ConcurrentDictionary<(string Owner, string Repo, int Number), string> _cache = new();

    public PullRequestAuthorCache(Func<string, string, int, CancellationToken, Task<string?>> fetch) => _fetch = fetch;

    public async Task<string?> GetAsync(string owner, string repo, int number, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue((owner, repo, number), out var cached))
            return cached;

        var login = await _fetch(owner, repo, number, cancellationToken);
        if (login is not null)
            _cache[(owner, repo, number)] = login;
        return login;
    }
}
