using Heimdall.Core.Models;

namespace Heimdall.Tests.TestSupport;

/// <summary>Builds <see cref="RunRecord"/>s with sensible defaults for tests.</summary>
internal static class RunBuilder
{
    public static RunRecord Run(
        RunStatus status,
        long runId = 1,
        int runNumber = 1,
        long workflowId = 100,
        string workflow = "CI",
        string owner = "octo",
        string repo = "demo",
        string branch = "main",
        string actor = "alice",
        string ev = "push",
        IReadOnlyList<int>? prNumbers = null,
        IReadOnlyList<string>? prAuthors = null) =>
        new(
            RunId: runId,
            WorkflowId: workflowId,
            WorkflowName: workflow,
            RepoOwner: owner,
            RepoName: repo,
            HeadBranch: branch,
            Event: ev,
            RunNumber: runNumber,
            Status: status,
            TriggeringActorLogin: actor,
            PullRequestNumbers: prNumbers ?? [],
            PullRequestAuthorLogins: prAuthors ?? [],
            HtmlUrl: $"https://github.com/{owner}/{repo}/actions/runs/{runId}",
            CreatedAt: DateTimeOffset.UnixEpoch);
}
