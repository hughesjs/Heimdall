using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;
using Heimdall.ViewModels;
using Shouldly;

namespace Heimdall.UiTests;

public class SettingsViewModelTests
{
    private static AppSettings SampleSettings() => new(
        Repos: [new RepoConfig("octo", "demo", "main")],
        Identity: new Identity("alice"),
        RuleToggles: new Dictionary<string, bool> { [TriggeredByMeRule.RuleId] = true },
        PollIntervalSeconds: 45,
        NotificationsEnabled: false);

    [Fact]
    public async Task Load_populates_from_persisted_settings()
    {
        var viewModel = new SettingsViewModel(new FakeSettingsStore(SampleSettings()), new FakeGitHubGateway(), new FakeNotificationManager());

        await viewModel.LoadAsync(default);

        viewModel.Login.ShouldBe("alice");
        viewModel.PollIntervalSeconds.ShouldBe(45);
        viewModel.NotificationsEnabled.ShouldBeFalse();
        viewModel.Repos.ShouldHaveSingleItem().Name.ShouldBe("demo");
        viewModel.Rules.ShouldHaveSingleItem().Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Add_repo_validates_and_appends_on_success()
    {
        var viewModel = new SettingsViewModel(new FakeSettingsStore(), new FakeGitHubGateway(), new FakeNotificationManager()) { NewRepo = "acme/app" };

        var added = await viewModel.AddRepoAsync(default);

        added.ShouldBeTrue();
        var entry = viewModel.Repos.ShouldHaveSingleItem();
        entry.Owner.ShouldBe("acme");
        entry.Name.ShouldBe("app");
        viewModel.NewRepo.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_repo_rejects_a_malformed_entry()
    {
        var viewModel = new SettingsViewModel(new FakeSettingsStore(), new FakeGitHubGateway(), new FakeNotificationManager()) { NewRepo = "not-a-repo" };

        (await viewModel.AddRepoAsync(default)).ShouldBeFalse();
        viewModel.Repos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_repo_reports_a_validation_failure_and_does_not_append()
    {
        var gateway = new FakeGitHubGateway { OnValidate = (_, _) => throw new InvalidOperationException("no access") };
        var viewModel = new SettingsViewModel(new FakeSettingsStore(), gateway, new FakeNotificationManager()) { NewRepo = "secret/repo" };

        (await viewModel.AddRepoAsync(default)).ShouldBeFalse();
        viewModel.Repos.ShouldBeEmpty();
        viewModel.StatusMessage.ShouldContain("no access");
    }

    [Fact]
    public async Task Save_persists_the_current_edits()
    {
        var store = new FakeSettingsStore(SampleSettings());
        var viewModel = new SettingsViewModel(store, new FakeGitHubGateway(), new FakeNotificationManager());
        await viewModel.LoadAsync(default);

        viewModel.Login = "bob";
        viewModel.PollIntervalSeconds = 120;
        await viewModel.SaveAsync(default);

        store.Saved.Identity.Login.ShouldBe("bob");
        store.Saved.PollIntervalSeconds.ShouldBe(120);
    }

    [Fact]
    public async Task Test_notification_fires_through_the_notification_manager()
    {
        var notifications = new FakeNotificationManager();
        var viewModel = new SettingsViewModel(new FakeSettingsStore(), new FakeGitHubGateway(), notifications);

        await viewModel.TestNotificationAsync();

        notifications.Shown.ShouldHaveSingleItem().Title.ShouldBe("Heimdall");
        viewModel.StatusMessage.ShouldBe("Test notification sent.");
    }

    [Fact]
    public async Task Load_builds_announce_toggles_from_the_repos_workflows()
    {
        var settings = new AppSettings(
            [new RepoConfig("octo", "demo", "main") { AnnounceWorkflows = ["CD"] }],
            new Identity("alice"),
            new Dictionary<string, bool>(),
            PollIntervalSeconds: 60,
            NotificationsEnabled: true);
        var gateway = new FakeGitHubGateway { OnGetWorkflows = _ => ["CI", "CD"] };
        var viewModel = new SettingsViewModel(new FakeSettingsStore(settings), gateway, new FakeNotificationManager());

        await viewModel.LoadAsync(default);

        var entry = viewModel.Repos.ShouldHaveSingleItem();
        entry.Workflows.Select(workflow => workflow.Name).ShouldBe(["CD", "CI"]);
        entry.Workflows.Single(workflow => workflow.Name == "CD").IsAnnounce.ShouldBeTrue();
    }
}
