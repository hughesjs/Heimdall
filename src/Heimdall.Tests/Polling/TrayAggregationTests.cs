using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Shouldly;
using static Heimdall.Tests.Polling.RunBuilder;

namespace Heimdall.Tests.Polling;

public class TrayAggregationTests
{
    private static PipelineState State(RunStatus settled, bool inProgress = false, string branch = "main") =>
        new(new PipelineKey("octo", "demo", 100, branch), settled, inProgress, 1, Run(settled, branch: branch));

    [Fact]
    public void Disconnected_is_grey_regardless_of_states()
    {
        PipelineStateMachine.Aggregate([State(RunStatus.Success)], connected: false).ShouldBe(TrayStatus.Grey);
    }

    [Fact]
    public void No_pipelines_is_green()
    {
        PipelineStateMachine.Aggregate([], connected: true).ShouldBe(TrayStatus.Green);
    }

    [Fact]
    public void All_success_is_green()
    {
        PipelineStateMachine.Aggregate(
            [State(RunStatus.Success, branch: "a"), State(RunStatus.Success, branch: "b")],
            connected: true).ShouldBe(TrayStatus.Green);
    }

    [Fact]
    public void Any_failure_is_red()
    {
        PipelineStateMachine.Aggregate(
            [State(RunStatus.Success, branch: "a"), State(RunStatus.Failure, branch: "b")],
            connected: true).ShouldBe(TrayStatus.Red);
    }

    [Fact]
    public void In_progress_without_failure_is_amber()
    {
        PipelineStateMachine.Aggregate(
            [State(RunStatus.Success, branch: "a"), State(RunStatus.Success, inProgress: true, branch: "b")],
            connected: true).ShouldBe(TrayStatus.Amber);
    }

    [Fact]
    public void Failure_beats_in_progress()
    {
        PipelineStateMachine.Aggregate(
            [State(RunStatus.Failure, branch: "a"), State(RunStatus.Success, inProgress: true, branch: "b")],
            connected: true).ShouldBe(TrayStatus.Red);
    }
}
