using Heimdall.Core.Models;

namespace Heimdall.Tests.Polling;

/// <summary>Builds <see cref="RunRecord"/>s with sensible defaults for state-machine tests.</summary>
internal static class RunBuilder
{
    public static RunRecord Run(
        RunStatus status,
        long runId = 1,
        int runNumber = 1,
        long workflowId = 100,
        string owner = "octo",
        string repo = "demo",
        string branch = "main",
        string actor = "alice",
        string ev = "push") =>
        new(
            RunId: runId,
            WorkflowId: workflowId,
            WorkflowName: "CI",
            RepoOwner: owner,
            RepoName: repo,
            HeadBranch: branch,
            Event: ev,
            RunNumber: runNumber,
            Status: status,
            TriggeringActorLogin: actor,
            PullRequestNumbers: [],
            PullRequestAuthorLogins: [],
            HtmlUrl: $"https://github.com/{owner}/{repo}/actions/runs/{runId}",
            CreatedAt: DateTimeOffset.UnixEpoch);
}
