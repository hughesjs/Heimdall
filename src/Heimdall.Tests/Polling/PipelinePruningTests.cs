using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Shouldly;
using static Heimdall.Tests.Polling.RunBuilder;

namespace Heimdall.Tests.Polling;

public class PipelinePruningTests
{
    private static (PipelineKey Key, PipelineState State) Line(RunStatus settled, bool inProgress, string branch)
    {
        var key = new PipelineKey("octo", "demo", 100, branch);
        return (key, new PipelineState(key, settled, inProgress, 1, Run(settled, branch: branch)));
    }

    [Fact]
    public void Keys_seen_this_cycle_are_retained()
    {
        var (key, state) = Line(RunStatus.Success, inProgress: false, "main");
        var map = new Dictionary<PipelineKey, PipelineState> { [key] = state };

        var pruned = PipelineStateMachine.Prune(map, new HashSet<PipelineKey> { key });

        pruned.ShouldContainKey(key);
    }

    [Theory]
    [InlineData(RunStatus.Success)]
    [InlineData(RunStatus.Unknown)]
    public void Unseen_non_failing_keys_are_dropped(RunStatus settled)
    {
        var (key, state) = Line(settled, inProgress: false, "stale");
        var map = new Dictionary<PipelineKey, PipelineState> { [key] = state };

        var pruned = PipelineStateMachine.Prune(map, new HashSet<PipelineKey>());

        pruned.ShouldNotContainKey(key);
    }

    [Fact]
    public void Unseen_failing_keys_are_retained_so_recovery_can_be_reported()
    {
        var (key, state) = Line(RunStatus.Failure, inProgress: false, "broken");
        var map = new Dictionary<PipelineKey, PipelineState> { [key] = state };

        var pruned = PipelineStateMachine.Prune(map, new HashSet<PipelineKey>());

        pruned.ShouldContainKey(key);
    }

    [Fact]
    public void Unseen_in_progress_but_non_failing_key_is_dropped()
    {
        var (key, state) = Line(RunStatus.Success, inProgress: true, "stale");
        var map = new Dictionary<PipelineKey, PipelineState> { [key] = state };

        var pruned = PipelineStateMachine.Prune(map, new HashSet<PipelineKey>());

        pruned.ShouldNotContainKey(key);
    }
}
