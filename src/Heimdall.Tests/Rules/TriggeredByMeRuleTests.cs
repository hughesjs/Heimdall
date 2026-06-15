using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Rules;

public class TriggeredByMeRuleTests
{
    private static readonly Identity Me = new("alice");
    private static readonly RepoConfig Repo = new("octo", "demo", "main");
    private readonly TriggeredByMeRule _rule = new();

    [Fact]
    public void Relevant_when_i_triggered_the_run()
    {
        _rule.IsRelevant(Run(RunStatus.Failure, actor: "alice"), Me, Repo).ShouldBeTrue();
    }

    [Fact]
    public void Login_match_is_case_insensitive()
    {
        _rule.IsRelevant(Run(RunStatus.Failure, actor: "ALICE"), Me, Repo).ShouldBeTrue();
    }

    [Fact]
    public void Not_relevant_when_someone_else_triggered_the_run()
    {
        _rule.IsRelevant(Run(RunStatus.Failure, actor: "bob"), Me, Repo).ShouldBeFalse();
    }

    [Fact]
    public void Is_on_by_default()
    {
        _rule.DefaultEnabled.ShouldBeTrue();
    }
}
