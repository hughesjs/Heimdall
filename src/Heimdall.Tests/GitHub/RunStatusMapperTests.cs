using Heimdall.Core.GitHub;
using Heimdall.Core.Models;
using Shouldly;

namespace Heimdall.Tests.GitHub;

public class RunStatusMapperTests
{
    [Theory]
    [InlineData("queued")]
    [InlineData("in_progress")]
    [InlineData("waiting")]
    [InlineData("pending")]
    [InlineData("requested")]
    [InlineData(null)]
    public void Non_completed_status_is_in_progress(string? status)
    {
        RunStatusMapper.FromApi(status, conclusion: null).ShouldBe(RunStatus.InProgress);
    }

    [Fact]
    public void Completed_success_is_success()
    {
        RunStatusMapper.FromApi("completed", "success").ShouldBe(RunStatus.Success);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("startup_failure")]
    public void Completed_failure_conclusions_are_failure(string conclusion)
    {
        RunStatusMapper.FromApi("completed", conclusion).ShouldBe(RunStatus.Failure);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("timed_out")]
    [InlineData("neutral")]
    [InlineData("skipped")]
    [InlineData("stale")]
    [InlineData("action_required")]
    [InlineData(null)]
    [InlineData("some_future_conclusion")]
    public void Other_completed_conclusions_are_unknown(string? conclusion)
    {
        RunStatusMapper.FromApi("completed", conclusion).ShouldBe(RunStatus.Unknown);
    }

    [Fact]
    public void Status_match_is_case_insensitive()
    {
        RunStatusMapper.FromApi("COMPLETED", "SUCCESS").ShouldBe(RunStatus.Success);
    }
}
