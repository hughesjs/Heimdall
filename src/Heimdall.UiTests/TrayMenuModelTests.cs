using Heimdall;
using Heimdall.Core.Models;
using Shouldly;

namespace Heimdall.UiTests;

public class TrayMenuModelTests
{
    private static PipelineState Pipeline(
        string owner, string repo, string branch, string workflow,
        RunStatus settled, bool inProgress = false, long workflowId = 1,
        bool countsTowardTray = true, DateTimeOffset? createdAt = null)
    {
        var run = new RunRecord(
            RunId: 1, WorkflowId: workflowId, WorkflowName: workflow,
            RepoOwner: owner, RepoName: repo, HeadBranch: branch, Event: "push",
            RunNumber: 1, Status: settled, TriggeringActorLogin: "alice",
            PullRequestNumbers: [], PullRequestAuthorLogins: [],
            HtmlUrl: $"https://github.com/{owner}/{repo}/actions/{workflow}/{branch}",
            CreatedAt: createdAt ?? DateTimeOffset.UnixEpoch);
        return new PipelineState(new PipelineKey(owner, repo, workflowId, branch), settled, inProgress, 1, run)
        {
            CountsTowardTray = countsTowardTray,
        };
    }

    [Fact]
    public void Groups_pipelines_under_their_repo_with_a_header_dot()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "build", RunStatus.Success),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
        ]);

        menu.Repos.Count.ShouldBe(1);
        menu.Repos[0].Header.ShouldBe("🟢 acme/web");
        menu.Repos[0].Pipelines.Count.ShouldBe(2);
        menu.RecentlyAnnounced.ShouldBeEmpty();
    }

    [Fact]
    public void Repo_health_is_failure_first()
    {
        // A repo with a failing line and an in-progress line reads as failing (🔴), matching the tray.
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "api", "dev", "ci", RunStatus.Success, inProgress: true, workflowId: 2),
        ]);

        menu.Repos.ShouldHaveSingleItem().Header.ShouldBe("🔴 acme/api");
    }

    [Fact]
    public void Repos_are_ordered_unhealthy_first_then_alphabetical()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "docs", "main", "ci", RunStatus.Success),
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "worker", "main", "ci", RunStatus.Success, inProgress: true),
        ]);

        menu.Repos.Select(g => g.Header).ShouldBe(
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
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "release", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "build", RunStatus.Success, workflowId: 1),
        ]);

        menu.Repos.ShouldHaveSingleItem().Pipelines.Select(p => p.Label).ShouldBe(
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
            .Repos.ShouldHaveSingleItem()
            .Pipelines.ShouldHaveSingleItem();

        entry.Dot.ShouldBe(dot);
        entry.Label.ShouldEndWith($"— {word}");
    }

    [Fact]
    public void Empty_input_produces_no_repos_and_no_announcements()
    {
        var menu = TrayMenuModel.Build([]);

        menu.Repos.ShouldBeEmpty();
        menu.RecentlyAnnounced.ShouldBeEmpty();
    }

    [Fact]
    public void Announce_only_pipelines_are_excluded_from_repos_and_listed_under_recently_announced()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "api", "main", "release", RunStatus.Failure, countsTowardTray: false),
        ]);

        menu.Repos.ShouldHaveSingleItem().Header.ShouldBe("🟢 acme/web");
        var announced = menu.RecentlyAnnounced.ShouldHaveSingleItem();
        announced.Dot.ShouldBe("🔴");
        announced.Label.ShouldBe("acme/api · release · main — failing");
    }

    [Fact]
    public void Announce_only_failure_does_not_redden_its_repo_dot()
    {
        // An announce-only failing run in the same repo must not affect the repo's health dot.
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "web", "main", "release", RunStatus.Failure, workflowId: 2, countsTowardTray: false),
        ]);

        menu.Repos.ShouldHaveSingleItem().Header.ShouldBe("🟢 acme/web");
    }

    [Fact]
    public void Recently_announced_is_newest_first_and_capped_at_ten()
    {
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pipelines = Enumerable.Range(0, 12)
            .Select(i => Pipeline(
                "acme", "repo" + i, "main", "release", RunStatus.Success,
                countsTowardTray: false, createdAt: epoch.AddDays(i)))
            .ToList();

        var announced = TrayMenuModel.Build(pipelines).RecentlyAnnounced;

        announced.Count.ShouldBe(10);
        announced[0].Label.ShouldBe("acme/repo11 · release · main — passing"); // newest
        announced[9].Label.ShouldBe("acme/repo2 · release · main — passing");  // 10th newest
    }
}
