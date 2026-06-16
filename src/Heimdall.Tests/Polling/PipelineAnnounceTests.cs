using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Polling;

public class PipelineAnnounceTests
{
    private static (PipelineState State, NotificationPayload? Notification) Announce(PipelineState? prior, RunRecord run, bool announceFailures = false) =>
        PipelineStateMachine.Apply(prior, run, NotifyPolicy.Announce, announceFailures);

    [Fact]
    public void First_sighting_of_a_settled_release_is_suppressed()
    {
        var (state, note) = Announce(prior: null, Run(RunStatus.Success, workflow: "CD"));

        note.ShouldBeNull();
        state.LastAnnouncedRunId.ShouldBe(1L);
    }

    [Fact]
    public void A_new_successful_run_announces_released()
    {
        var (seeded, _) = Announce(null, Run(RunStatus.Success, workflow: "CD"));
        var (state, note) = Announce(seeded, Run(RunStatus.Success, runId: 2, runNumber: 2, workflow: "CD"));

        note.ShouldNotBeNull();
        note.Kind.ShouldBe(NotificationKind.Released);
        note.Run.RunId.ShouldBe(2L);
        state.LastAnnouncedRunId.ShouldBe(2L);
    }

    [Fact]
    public void An_in_progress_run_then_settling_announces_once()
    {
        // First sighting is in-progress: no settled release to suppress, so the marker stays null.
        var (running, n1) = Announce(null, Run(RunStatus.InProgress, runId: 1, runNumber: 1, workflow: "CD"));
        n1.ShouldBeNull();
        running.LastAnnouncedRunId.ShouldBeNull();

        var (settled, n2) = Announce(running, Run(RunStatus.Success, runId: 1, runNumber: 1, workflow: "CD"));
        n2.ShouldNotBeNull();
        n2.Kind.ShouldBe(NotificationKind.Released);

        // Re-seeing the same settled run does not re-announce.
        var (_, n3) = Announce(settled, Run(RunStatus.Success, runId: 1, runNumber: 1, workflow: "CD"));
        n3.ShouldBeNull();
    }

    [Fact]
    public void Consecutive_successful_runs_each_announce()
    {
        var (s1, _) = Announce(null, Run(RunStatus.Success, runId: 1, runNumber: 1, workflow: "CD"));
        var (s2, n2) = Announce(s1, Run(RunStatus.Success, runId: 2, runNumber: 2, workflow: "CD"));
        var (_, n3) = Announce(s2, Run(RunStatus.Success, runId: 3, runNumber: 3, workflow: "CD"));

        n2.ShouldNotBeNull();
        n2.Kind.ShouldBe(NotificationKind.Released);
        n3.ShouldNotBeNull();
        n3.Kind.ShouldBe(NotificationKind.Released);
    }

    [Fact]
    public void A_failure_announces_broke_when_announce_failures_is_on()
    {
        var (seeded, _) = Announce(null, Run(RunStatus.Success, workflow: "CD"), announceFailures: true);
        var (_, note) = Announce(seeded, Run(RunStatus.Failure, runId: 2, runNumber: 2, workflow: "CD"), announceFailures: true);

        note.ShouldNotBeNull();
        note.Kind.ShouldBe(NotificationKind.Broke);
    }

    [Fact]
    public void A_failure_is_silent_when_announce_failures_is_off()
    {
        var (seeded, _) = Announce(null, Run(RunStatus.Success, workflow: "CD"));
        var (state, note) = Announce(seeded, Run(RunStatus.Failure, runId: 2, runNumber: 2, workflow: "CD"));

        note.ShouldBeNull();
        // A skipped failure still marks the run as seen, so it never announces retroactively.
        state.LastAnnouncedRunId.ShouldBe(2L);
    }

    [Fact]
    public void The_same_run_id_is_not_re_announced_across_polls()
    {
        var (seeded, _) = Announce(null, Run(RunStatus.Success, runId: 1, runNumber: 1, workflow: "CD"));
        var (after, n1) = Announce(seeded, Run(RunStatus.Success, runId: 2, runNumber: 2, workflow: "CD"));
        n1.ShouldNotBeNull();

        var (_, n2) = Announce(after, Run(RunStatus.Success, runId: 2, runNumber: 2, workflow: "CD"));
        n2.ShouldBeNull();
    }
}
