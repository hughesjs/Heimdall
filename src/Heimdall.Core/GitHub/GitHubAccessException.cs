namespace Heimdall.Core.GitHub;

/// <summary>Why a GitHub call failed, in domain terms (Octokit exception types don't leak above the gateway).</summary>
public enum GitHubAccessError
{
    /// <summary>Token missing, expired, or revoked (401/403) — re-auth required.</summary>
    Unauthorised,

    /// <summary>Primary or secondary rate limit hit — back off until reset.</summary>
    RateLimited
}

/// <summary>A GitHub access failure surfaced to the polling layer without exposing Octokit types.</summary>
public sealed class GitHubAccessException(GitHubAccessError error, Exception innerException)
    : Exception(innerException.Message, innerException)
{
    public GitHubAccessError Error { get; } = error;
}
