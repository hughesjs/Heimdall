using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Rules;

public class DefaultBranchBreakingRuleTests
{
    private static readonly Identity Me = new("alice");
    private static readonly RepoConfig Repo = new("octo", "demo", "main");
    private readonly DefaultBranchBreakingRule _rule = new();

    [Fact]
    public void Relevant_when_the_run_is_on_the_default_branch_regardless_of_actor()
    {
        var run = Run(RunStatus.Failure, branch: "main", actor: "stranger");
        _rule.IsRelevant(run, Me, Repo).ShouldBeTrue();
    }

    [Fact]
    public void Not_relevant_on_a_non_default_branch()
    {
        var run = Run(RunStatus.Failure, branch: "feature/x", actor: "stranger");
        _rule.IsRelevant(run, Me, Repo).ShouldBeFalse();
    }

    [Fact]
    public void Branch_match_is_case_sensitive()
    {
        var run = Run(RunStatus.Failure, branch: "Main", actor: "stranger");
        _rule.IsRelevant(run, Me, Repo).ShouldBeFalse();
    }

    [Fact]
    public void Is_off_by_default()
    {
        _rule.DefaultEnabled.ShouldBeFalse();
    }
}
