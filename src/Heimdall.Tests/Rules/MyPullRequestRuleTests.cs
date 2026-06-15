using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Rules;

public class MyPullRequestRuleTests
{
    private static readonly Identity Me = new("alice");
    private static readonly RepoConfig Repo = new("octo", "demo", "main");
    private readonly MyPullRequestRule _rule = new();

    [Fact]
    public void Relevant_when_i_authored_the_runs_pull_request()
    {
        var run = Run(RunStatus.Failure, actor: "bob", prAuthors: ["alice"]);
        _rule.IsRelevant(run, Me, Repo).ShouldBeTrue();
    }

    [Fact]
    public void Author_match_is_case_insensitive()
    {
        var run = Run(RunStatus.Failure, actor: "bob", prAuthors: ["Alice"]);
        _rule.IsRelevant(run, Me, Repo).ShouldBeTrue();
    }

    [Fact]
    public void Not_relevant_when_the_pr_was_authored_by_someone_else()
    {
        var run = Run(RunStatus.Failure, actor: "bob", prAuthors: ["carol"]);
        _rule.IsRelevant(run, Me, Repo).ShouldBeFalse();
    }

    [Fact]
    public void Not_relevant_when_there_are_no_pull_requests()
    {
        _rule.IsRelevant(Run(RunStatus.Failure, actor: "bob"), Me, Repo).ShouldBeFalse();
    }

    [Fact]
    public void Is_on_by_default()
    {
        _rule.DefaultEnabled.ShouldBeTrue();
    }
}
