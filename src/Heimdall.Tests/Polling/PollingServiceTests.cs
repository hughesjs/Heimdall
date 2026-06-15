using Heimdall.Core.GitHub;
using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;
using Heimdall.Tests.TestSupport;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Polling;

public class PollingServiceTests
{
    private static AppSettings Settings(bool notifications = true) => new(
        Repos: [new RepoConfig("octo", "demo", "main")],
        Identity: new Identity("alice"),
        RuleToggles: new Dictionary<string, bool> { [TriggeredByMeRule.RuleId] = true },
        PollIntervalSeconds: 60,
        NotificationsEnabled: notifications);

    private static PollingService NewService(FakeGitHubGateway gateway) => new(gateway, new RelevanceEngine(StandardRules.All));

    [Fact]
    public async Task Detects_broke_then_recover_across_cycles()
    {
        var gateway = new FakeGitHubGateway();
        var service = NewService(gateway);
        var transitions = new List<NotificationPayload>();
        service.Transition += transitions.Add;

        gateway.OnGetRuns = _ => [Run(RunStatus.Success, runId: 1, runNumber: 1, actor: "alice")];
        await service.PollOnceAsync(Settings(), default); // silent seed

        gateway.OnGetRuns = _ => [Run(RunStatus.Failure, runId: 2, runNumber: 2, actor: "alice")];
        await service.PollOnceAsync(Settings(), default); // broke

        gateway.OnGetRuns = _ => [Run(RunStatus.Success, runId: 3, runNumber: 3, actor: "alice")];
        await service.PollOnceAsync(Settings(), default); // recover

        transitions.Select(t => t.Kind).ShouldBe([TransitionKind.Broke, TransitionKind.Recovered]);
    }

    [Fact]
    public async Task Picks_the_latest_run_per_pipeline_within_a_cycle()
    {
        var gateway = new FakeGitHubGateway();
        var service = NewService(gateway);
        var transitions = new List<NotificationPayload>();
        service.Transition += transitions.Add;

        gateway.OnGetRuns = _ => [Run(RunStatus.Success, runId: 1, runNumber: 1, actor: "alice")];
        await service.PollOnceAsync(Settings(), default); // seed Success

        // Newest-first page: the failure (run 3) is the latest for the pipeline and should win.
        gateway.OnGetRuns = _ =>
        [
            Run(RunStatus.Failure, runId: 3, runNumber: 3, actor: "alice"),
            Run(RunStatus.Success, runId: 2, runNumber: 2, actor: "alice")
        ];
        await service.PollOnceAsync(Settings(), default);

        transitions.Single().Kind.ShouldBe(TransitionKind.Broke);
    }

    [Fact]
    public async Task Irrelevant_runs_do_not_transition()
    {
        var gateway = new FakeGitHubGateway();
        var service = NewService(gateway);
        var transitions = new List<NotificationPayload>();
        service.Transition += transitions.Add;

        gateway.OnGetRuns = _ => [Run(RunStatus.Success, actor: "bob")];
        await service.PollOnceAsync(Settings(), default);
        gateway.OnGetRuns = _ => [Run(RunStatus.Failure, runId: 2, runNumber: 2, actor: "bob")];
        await service.PollOnceAsync(Settings(), default);

        transitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_notifications_suppress_transition_events()
    {
        var gateway = new FakeGitHubGateway();
        var service = NewService(gateway);
        var transitions = new List<NotificationPayload>();
        service.Transition += transitions.Add;

        gateway.OnGetRuns = _ => [Run(RunStatus.Success, actor: "alice")];
        await service.PollOnceAsync(Settings(notifications: false), default);
        gateway.OnGetRuns = _ => [Run(RunStatus.Failure, runId: 2, runNumber: 2, actor: "alice")];
        await service.PollOnceAsync(Settings(notifications: false), default);

        transitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Aggregate_is_red_when_a_relevant_pipeline_is_failing()
    {
        var gateway = new FakeGitHubGateway { OnGetRuns = _ => [Run(RunStatus.Failure, actor: "alice")] };
        var service = NewService(gateway);
        TrayStatus? last = null;
        service.Aggregate += status => last = status;

        await service.PollOnceAsync(Settings(), default);

        last.ShouldBe(TrayStatus.Red);
    }

    [Fact]
    public async Task Auth_failure_greys_the_tray_and_signals_reauth()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => throw new GitHubAccessException(GitHubAccessError.Unauthorised, new InvalidOperationException("401"))
        };
        var service = NewService(gateway);
        TrayStatus? last = null;
        var authFailed = false;
        service.Aggregate += status => last = status;
        service.AuthenticationFailed += () => authFailed = true;

        await service.PollOnceAsync(Settings(), default);

        last.ShouldBe(TrayStatus.Grey);
        authFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task Non_auth_failure_greys_the_tray_and_reports_the_error()
    {
        var boom = new InvalidOperationException("network down");
        var gateway = new FakeGitHubGateway { OnGetRuns = _ => throw boom };
        var service = NewService(gateway);
        TrayStatus? last = null;
        Exception? reported = null;
        service.Aggregate += status => last = status;
        service.PollFailed += ex => reported = ex;

        await service.PollOnceAsync(Settings(), default);

        last.ShouldBe(TrayStatus.Grey);
        reported.ShouldBe(boom);
    }

    [Fact]
    public async Task Snapshot_reports_the_current_pipelines()
    {
        var gateway = new FakeGitHubGateway { OnGetRuns = _ => [Run(RunStatus.Success, actor: "alice")] };
        var service = NewService(gateway);
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Snapshot += states => snapshot = states;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.Count.ShouldBe(1);
    }
}
