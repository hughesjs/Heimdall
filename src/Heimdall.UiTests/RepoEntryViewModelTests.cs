using Heimdall.Core.Models;
using Heimdall.ViewModels;
using Shouldly;

namespace Heimdall.UiTests;

public class RepoEntryViewModelTests
{
    [Fact]
    public void Maps_a_repo_config_to_editable_fields()
    {
        var repo = new RepoConfig("octo", "demo", "main") { AnnounceWorkflows = ["CD", "Release"], AnnounceFailures = true };

        var vm = new RepoEntryViewModel(repo);

        vm.Owner.ShouldBe("octo");
        vm.Name.ShouldBe("demo");
        vm.DefaultBranch.ShouldBe("main");
        vm.AnnounceWorkflowsText.ShouldBe("CD, Release");
        vm.AnnounceFailures.ShouldBeTrue();
    }

    [Fact]
    public void Builds_a_repo_config_splitting_trimming_and_dropping_empties()
    {
        var vm = new RepoEntryViewModel(new RepoConfig("octo", "demo", "main"))
        {
            AnnounceWorkflowsText = " CD , , Release ,",
            AnnounceFailures = true
        };

        var repo = vm.ToRepoConfig();

        repo.Owner.ShouldBe("octo");
        repo.Name.ShouldBe("demo");
        repo.DefaultBranch.ShouldBe("main");
        repo.AnnounceWorkflows.ShouldBe(["CD", "Release"]);
        repo.AnnounceFailures.ShouldBeTrue();
    }

    [Fact]
    public void Empty_announce_text_yields_no_workflows()
    {
        var repo = new RepoEntryViewModel(new RepoConfig("octo", "demo", "main")).ToRepoConfig();

        repo.AnnounceWorkflows.ShouldBeEmpty();
        repo.AnnounceFailures.ShouldBeFalse();
    }
}
