using Heimdall.Core.GitHub;
using Heimdall.Core.Models;

namespace Heimdall.Tests.TestSupport;

/// <summary>An in-memory <see cref="IGitHubGateway"/> for driving the polling service through scripted cycles.</summary>
internal sealed class FakeGitHubGateway : IGitHubGateway
{
    /// <summary>Supplies the runs returned for a repo each cycle. May throw to simulate failures.</summary>
    public Func<RepoConfig, IReadOnlyList<RunRecord>> OnGetRuns { get; set; } = _ => [];

    public RateLimitInfo? LastRateLimit { get; set; }

    public Task<RepoConfig> ValidateAndDescribeAsync(string owner, string name, CancellationToken cancellationToken) =>
        Task.FromResult(new RepoConfig(owner, name, "main"));

    public Task<IReadOnlyList<RunRecord>> GetRecentRunsAsync(RepoConfig repo, CancellationToken cancellationToken) =>
        Task.FromResult(OnGetRuns(repo));
}
