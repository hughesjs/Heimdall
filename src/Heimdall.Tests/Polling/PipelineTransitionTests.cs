using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Shouldly;
using static Heimdall.Tests.Polling.RunBuilder;

namespace Heimdall.Tests.Polling;

public class PipelineTransitionTests
{
    [Theory]
    [InlineData(RunStatus.Success)]
    [InlineData(RunStatus.Failure)]
    [InlineData(RunStatus.Unknown)]
    [InlineData(RunStatus.InProgress)]
    public void First_sighting_seeds_silently(RunStatus status)
    {
        var (state, note) = PipelineStateMachine.Apply(prior: null, Run(status));

        note.ShouldBeNull();
        state.LastRunId.ShouldBe(1L);
    }

    [Fact]
    public void First_settled_sighting_anchors_to_that_status()
    {
        var (state, _) = PipelineStateMachine.Apply(prior: null, Run(RunStatus.Failure));
        state.LastSettledStatus.ShouldBe(RunStatus.Failure);
        state.InProgress.ShouldBeFalse();
    }

    [Fact]
    public void Success_to_failure_notifies_broke()
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(RunStatus.Success));
        var (next, note) = PipelineStateMachine.Apply(seeded, Run(RunStatus.Failure, runId: 2, runNumber: 2));

        note.ShouldNotBeNull();
        note.Kind.ShouldBe(TransitionKind.Broke);
        note.Run.RunId.ShouldBe(2L);
        next.LastSettledStatus.ShouldBe(RunStatus.Failure);
    }

    [Fact]
    public void Failure_to_success_notifies_recovered()
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(RunStatus.Failure));
        var (next, note) = PipelineStateMachine.Apply(seeded, Run(RunStatus.Success, runId: 2, runNumber: 2));

        note.ShouldNotBeNull();
        note.Kind.ShouldBe(TransitionKind.Recovered);
        next.LastSettledStatus.ShouldBe(RunStatus.Success);
    }

    [Theory]
    [InlineData(RunStatus.Success)]
    [InlineData(RunStatus.Failure)]
    public void Unchanged_settled_status_does_not_notify(RunStatus status)
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(status));
        var (_, note) = PipelineStateMachine.Apply(seeded, Run(status, runId: 2, runNumber: 2));

        note.ShouldBeNull();
    }

    [Fact]
    public void In_progress_run_holds_the_settled_anchor()
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(RunStatus.Success));
        var (state, note) = PipelineStateMachine.Apply(seeded, Run(RunStatus.InProgress, runId: 2, runNumber: 2));

        note.ShouldBeNull();
        state.InProgress.ShouldBeTrue();
        state.LastSettledStatus.ShouldBe(RunStatus.Success);
    }

    [Fact]
    public void Transition_fires_when_an_in_progress_run_later_settles()
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(RunStatus.Success));
        var (running, _) = PipelineStateMachine.Apply(seeded, Run(RunStatus.InProgress, runId: 2, runNumber: 2));
        var (settled, note) = PipelineStateMachine.Apply(running, Run(RunStatus.Failure, runId: 2, runNumber: 2));

        note.ShouldNotBeNull();
        note.Kind.ShouldBe(TransitionKind.Broke);
        settled.InProgress.ShouldBeFalse();
    }

    [Fact]
    public void Unknown_run_does_not_overwrite_the_anchor_so_recovery_is_still_detected()
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(RunStatus.Failure));
        var (afterCancelled, n1) = PipelineStateMachine.Apply(seeded, Run(RunStatus.Unknown, runId: 2, runNumber: 2));
        n1.ShouldBeNull();
        afterCancelled.LastSettledStatus.ShouldBe(RunStatus.Failure);

        var (recovered, n2) = PipelineStateMachine.Apply(afterCancelled, Run(RunStatus.Success, runId: 3, runNumber: 3));
        n2.ShouldNotBeNull();
        n2.Kind.ShouldBe(TransitionKind.Recovered);
    }

    [Fact]
    public void Settling_from_an_unknown_only_baseline_does_not_notify()
    {
        var (seeded, _) = PipelineStateMachine.Apply(null, Run(RunStatus.Unknown));
        var (state, note) = PipelineStateMachine.Apply(seeded, Run(RunStatus.Failure, runId: 2, runNumber: 2));

        note.ShouldBeNull(); // never had a real Success/Failure baseline
        state.LastSettledStatus.ShouldBe(RunStatus.Failure);
    }
}
