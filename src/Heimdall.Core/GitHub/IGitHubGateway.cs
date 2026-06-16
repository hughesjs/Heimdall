using Heimdall.Core.Models;

namespace Heimdall.Core.GitHub;

/// <summary>
/// The app's narrow view of GitHub: validate/describe a repo, list its recent runs as normalised
/// <see cref="RunRecord"/>s (PR authors enriched), and surface the latest rate-limit snapshot.
/// Octokit types never leak above this seam.
/// </summary>
public interface IGitHubGateway
{
    /// <summary>Confirms access to a repo and resolves its default branch.</summary>
    Task<RepoConfig> ValidateAndDescribeAsync(string owner, string name, CancellationToken cancellationToken);

    /// <summary>Returns the most recent page of runs for a repo, newest first.</summary>
    Task<IReadOnlyList<RunRecord>> GetRecentRunsAsync(RepoConfig repo, CancellationToken cancellationToken);

    /// <summary>Lists the repo's workflow names (for choosing announce workflows in settings).</summary>
    Task<IReadOnlyList<string>> GetWorkflowNamesAsync(RepoConfig repo, CancellationToken cancellationToken);

    /// <summary>Lists "owner/repo" for every repo the user can access (their own plus orgs they belong to), for autocomplete.</summary>
    Task<IReadOnlyList<string>> GetAccessibleRepositoriesAsync(CancellationToken cancellationToken);

    /// <summary>The rate limit reported by the most recent API call, if any.</summary>
    RateLimitInfo? LastRateLimit { get; }
}
