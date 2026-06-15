namespace Heimdall.Core.Models;

/// <summary>
/// A normalised projection of a GitHub Actions workflow run, carrying everything the relevance
/// rules and notifications need so no extra API calls are required downstream.
/// <see cref="PullRequestAuthorLogins"/> is enriched by the GitHub gateway (the run payload alone
/// does not include PR authors).
/// </summary>
public record RunRecord(
    long RunId,
    long WorkflowId,
    string WorkflowName,
    string RepoOwner,
    string RepoName,
    string HeadBranch,
    string Event,
    int RunNumber,
    RunStatus Status,
    string TriggeringActorLogin,
    IReadOnlyList<int> PullRequestNumbers,
    IReadOnlyList<string> PullRequestAuthorLogins,
    string HtmlUrl,
    DateTimeOffset CreatedAt);
