using Heimdall.Core.Models;
using Octokit;

namespace Heimdall.Core.GitHub;

/// <summary>
/// Octokit-backed <see cref="IGitHubGateway"/>: lists one page of recent runs, maps them to
/// <see cref="RunRecord"/>s, enriches PR authors via a permanent cache, and tracks the rate limit.
/// </summary>
public sealed class GitHubGateway : IGitHubGateway
{
    private const int RecentRunsPageSize = 50;

    private readonly IGitHubClient _client;
    private readonly PullRequestAuthorCache _authors;

    public GitHubGateway(IGitHubClient client)
    {
        _client = client;
        _authors = new PullRequestAuthorCache(async (owner, repo, number, _) =>
        {
            var pr = await _client.PullRequest.Get(owner, repo, number);
            CaptureRateLimit();
            return pr.User?.Login;
        });
    }

    public RateLimitInfo? LastRateLimit { get; private set; }

    public async Task<RepoConfig> ValidateAndDescribeAsync(string owner, string name, CancellationToken cancellationToken)
    {
        var repo = await _client.Repository.Get(owner, name);
        CaptureRateLimit();
        return new RepoConfig(owner, name, repo.DefaultBranch);
    }

    public async Task<IReadOnlyList<RunRecord>> GetRecentRunsAsync(RepoConfig repo, CancellationToken cancellationToken)
    {
        var response = await _client.Actions.Workflows.Runs.List(
            repo.Owner, repo.Name, new WorkflowRunsRequest(), new ApiOptions { PageSize = RecentRunsPageSize, PageCount = 1 });
        CaptureRateLimit();

        var records = new List<RunRecord>(response.WorkflowRuns.Count);
        foreach (var run in response.WorkflowRuns)
            records.Add(await MapAsync(run, repo, cancellationToken));
        return records;
    }

    private async Task<RunRecord> MapAsync(WorkflowRun run, RepoConfig repo, CancellationToken cancellationToken)
    {
        var prNumbers = run.PullRequests?.Select(pr => pr.Number).ToList() ?? [];
        var authorLogins = new List<string>(prNumbers.Count);
        foreach (var number in prNumbers)
        {
            var login = await _authors.GetAsync(repo.Owner, repo.Name, number, cancellationToken);
            if (login is not null)
                authorLogins.Add(login);
        }

        return new RunRecord(
            RunId: run.Id,
            WorkflowId: run.WorkflowId,
            WorkflowName: run.Name,
            RepoOwner: repo.Owner,
            RepoName: repo.Name,
            HeadBranch: run.HeadBranch,
            Event: run.Event,
            RunNumber: (int)run.RunNumber,
            Status: RunStatusMapper.FromApi(run.Status.StringValue, run.Conclusion?.StringValue),
            TriggeringActorLogin: run.TriggeringActor?.Login ?? run.Actor?.Login ?? string.Empty,
            PullRequestNumbers: prNumbers,
            PullRequestAuthorLogins: authorLogins,
            HtmlUrl: run.HtmlUrl,
            CreatedAt: run.CreatedAt);
    }

    private void CaptureRateLimit()
    {
        var rateLimit = _client.GetLastApiInfo()?.RateLimit;
        if (rateLimit is not null)
            LastRateLimit = new RateLimitInfo(rateLimit.Remaining, rateLimit.Limit, rateLimit.Reset);
    }
}
