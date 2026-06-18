using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;
using Heimdall.Tests.TestSupport;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Polling;

public class PipelineStalenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private static AppSettings Settings() => new(
        Repos: [new RepoConfig("octo", "demo", "main")],
        Identity: new Identity("alice"),
        RuleToggles: new Dictionary<string, bool> { [TriggeredByMeRule.RuleId] = true },
        PollIntervalSeconds: 60,
        NotificationsEnabled: true);

    private static PollingService NewService(FakeGitHubGateway gateway, TimeProvider clock) =>
        new(gateway, new RelevanceEngine(StandardRules.All), clock);

    [Fact]
    public async Task Pipelines_older_than_the_window_are_excluded()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Failure, actor: "alice", createdAt: Now.AddDays(-31))]
        };
        var service = NewService(gateway, new TestTimeProvider(Now));
        TrayStatus? tray = null;
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Aggregate += s => tray = s;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.ShouldBeEmpty();
        tray.ShouldBe(TrayStatus.Green);
    }

    [Fact]
    public async Task Pipelines_within_the_window_are_kept()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Success, actor: "alice", createdAt: Now.AddDays(-29))]
        };
        var service = NewService(gateway, new TestTimeProvider(Now));
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_pipeline_exactly_at_the_window_boundary_is_kept()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Success, actor: "alice", createdAt: Now.AddDays(-30))]
        };
        var service = NewService(gateway, new TestTimeProvider(Now));
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_line_is_evicted_once_it_ages_out_despite_prune_retention()
    {
        var start = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestTimeProvider(start);
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Failure, actor: "alice", createdAt: start)]
        };
        var service = NewService(gateway, clock);
        TrayStatus? tray = null;
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Aggregate += s => tray = s;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default); // tracked while fresh
        tray.ShouldBe(TrayStatus.Red);

        // Branch goes quiet (no runs) and wall-clock advances past the window.
        gateway.OnGetRuns = _ => [];
        clock.Now = start.AddDays(31);
        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.ShouldBeEmpty();
        tray.ShouldBe(TrayStatus.Green);
    }
}
