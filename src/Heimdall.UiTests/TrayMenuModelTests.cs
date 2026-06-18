using Heimdall;
using Heimdall.Core.Models;
using Shouldly;

namespace Heimdall.UiTests;

public class TrayMenuModelTests
{
    private static PipelineState Pipeline(
        string owner, string repo, string branch, string workflow,
        RunStatus settled, bool inProgress = false, long workflowId = 1)
    {
        var run = new RunRecord(
            RunId: 1, WorkflowId: workflowId, WorkflowName: workflow,
            RepoOwner: owner, RepoName: repo, HeadBranch: branch, Event: "push",
            RunNumber: 1, Status: settled, TriggeringActorLogin: "alice",
            PullRequestNumbers: [], PullRequestAuthorLogins: [],
            HtmlUrl: $"https://github.com/{owner}/{repo}/actions/{workflow}/{branch}",
            CreatedAt: DateTimeOffset.UnixEpoch);
        return new PipelineState(new PipelineKey(owner, repo, workflowId, branch), settled, inProgress, 1, run);
    }

    [Fact]
    public void Groups_pipelines_under_their_repo_with_a_header_dot()
    {
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "build", RunStatus.Success),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
        ]);

        groups.Count.ShouldBe(1);
        groups[0].Header.ShouldBe("🟢 acme/web");
        groups[0].Pipelines.Count.ShouldBe(2);
    }

    [Fact]
    public void Repo_health_is_failure_first()
    {
        // A repo with a failing line and an in-progress line reads as failing (🔴), matching the tray.
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "api", "dev", "ci", RunStatus.Success, inProgress: true, workflowId: 2),
        ]);

        groups.ShouldHaveSingleItem().Header.ShouldBe("🔴 acme/api");
    }

    [Fact]
    public void Repos_are_ordered_unhealthy_first_then_alphabetical()
    {
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "docs", "main", "ci", RunStatus.Success),
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "worker", "main", "ci", RunStatus.Success, inProgress: true),
        ]);

        groups.Select(g => g.Header).ShouldBe(
        [
            "🔴 acme/api",     // failing
            "🟡 acme/worker",  // running
            "🟢 acme/docs",    // passing, alphabetical
            "🟢 acme/web",
        ]);
    }

    [Fact]
    public void Pipelines_within_a_repo_are_ordered_by_workflow_then_branch()
    {
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "release", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "build", RunStatus.Success, workflowId: 1),
        ]);

        groups.ShouldHaveSingleItem().Pipelines.Select(p => p.Label).ShouldBe(
        [
            "build · main — passing",
            "deploy · main — passing",
            "deploy · release — passing",
        ]);
    }

    [Theory]
    [InlineData(RunStatus.Failure, false, "🔴", "failing")]
    [InlineData(RunStatus.Success, true, "🟡", "running")]
    [InlineData(RunStatus.Success, false, "🟢", "passing")]
    [InlineData(RunStatus.Unknown, false, "⚪", "unknown")]
    public void Maps_each_state_to_its_dot_and_word(RunStatus settled, bool inProgress, string dot, string word)
    {
        var entry = TrayMenuModel
            .Build([Pipeline("acme", "web", "main", "ci", settled, inProgress)])
            .ShouldHaveSingleItem()
            .Pipelines.ShouldHaveSingleItem();

        entry.Dot.ShouldBe(dot);
        entry.Label.ShouldEndWith($"— {word}");
    }
}
