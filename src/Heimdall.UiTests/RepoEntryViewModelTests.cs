using Heimdall.Core.Models;
using Heimdall.ViewModels;
using Shouldly;

namespace Heimdall.UiTests;

public class RepoEntryViewModelTests
{
    [Fact]
    public void Builds_toggles_from_available_workflows_with_configured_ones_ticked()
    {
        var repo = new RepoConfig("octo", "demo", "main") { AnnounceWorkflows = ["CD"], AnnounceFailures = true };

        var vm = new RepoEntryViewModel(repo, ["CI", "CD", "Docs"]);

        vm.Owner.ShouldBe("octo");
        vm.Name.ShouldBe("demo");
        vm.DefaultBranch.ShouldBe("main");
        vm.AnnounceFailures.ShouldBeTrue();
        vm.Workflows.Select(workflow => workflow.Name).ShouldBe(["CD", "CI", "Docs"]); // sorted, case-insensitive
        vm.Workflows.Single(workflow => workflow.Name == "CD").IsAnnounce.ShouldBeTrue();
        vm.Workflows.Single(workflow => workflow.Name == "CI").IsAnnounce.ShouldBeFalse();
    }

    [Fact]
    public void Configured_workflows_missing_from_the_fetched_list_still_appear_ticked()
    {
        var repo = new RepoConfig("octo", "demo", "main") { AnnounceWorkflows = ["Renamed"] };

        var vm = new RepoEntryViewModel(repo, ["CI"]); // the fetched list no longer contains "Renamed"

        vm.Workflows.Single(workflow => workflow.Name == "Renamed").IsAnnounce.ShouldBeTrue();
    }

    [Fact]
    public void Build_collects_only_the_ticked_workflows()
    {
        var vm = new RepoEntryViewModel(new RepoConfig("octo", "demo", "main"), ["CI", "CD"]) { AnnounceFailures = true };
        vm.Workflows.Single(workflow => workflow.Name == "CD").IsAnnounce = true;

        var repo = vm.ToRepoConfig();

        repo.AnnounceWorkflows.ShouldBe(["CD"]);
        repo.AnnounceFailures.ShouldBeTrue();
    }

    [Fact]
    public void No_ticked_workflows_yields_no_announce_workflows()
    {
        var repo = new RepoEntryViewModel(new RepoConfig("octo", "demo", "main"), ["CI"]).ToRepoConfig();

        repo.AnnounceWorkflows.ShouldBeEmpty();
        repo.AnnounceFailures.ShouldBeFalse();
    }
}
