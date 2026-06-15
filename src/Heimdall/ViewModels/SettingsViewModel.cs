using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.Core.GitHub;
using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;

namespace Heimdall.ViewModels;

/// <summary>
/// Edits the watched repos, identity, rule toggles, poll interval, and notification preference, then
/// persists them. Adding a repo validates access via the gateway first. UI-framework-free, so the
/// add/remove/build/save logic is unit-tested directly.
/// </summary>
public sealed partial class SettingsViewModel(ISettingsStore store, IGitHubGateway gateway) : ViewModelBase
{
    private static readonly IReadOnlyDictionary<string, string> RuleNames = new Dictionary<string, string>
    {
        [TriggeredByMeRule.RuleId] = "Runs I triggered",
        [MyPullRequestRule.RuleId] = "PRs I opened",
        [DefaultBranchBreakingRule.RuleId] = "Default branch breaking"
    };

    public ObservableCollection<RepoConfig> Repos { get; } = [];
    public ObservableCollection<RuleToggle> Rules { get; } = [];

    [ObservableProperty]
    private string _login = string.Empty;

    [ObservableProperty]
    private int _pollIntervalSeconds = AppSettings.DefaultPollIntervalSeconds;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private string _newRepo = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Populates the view model from persisted settings.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await store.LoadAsync(cancellationToken);
        Login = settings.Identity.Login;
        PollIntervalSeconds = settings.PollIntervalSeconds;
        NotificationsEnabled = settings.NotificationsEnabled;

        Repos.Clear();
        foreach (var repo in settings.Repos)
            Repos.Add(repo);

        Rules.Clear();
        foreach (var (ruleId, enabled) in settings.RuleToggles)
            Rules.Add(new RuleToggle(ruleId, RuleNames.GetValueOrDefault(ruleId, ruleId), enabled));
    }

    /// <summary>Validates access to <see cref="NewRepo"/> (<c>owner/repo</c>) and adds it on success.</summary>
    public async Task<bool> AddRepoAsync(CancellationToken cancellationToken)
    {
        var parts = NewRepo.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            StatusMessage = "Enter a repository as owner/repo.";
            return false;
        }

        if (Repos.Any(repo => repo.Owner == parts[0] && repo.Name == parts[1]))
        {
            StatusMessage = "That repository is already in the list.";
            return false;
        }

        try
        {
            var repo = await gateway.ValidateAndDescribeAsync(parts[0], parts[1], cancellationToken);
            Repos.Add(repo);
            NewRepo = string.Empty;
            StatusMessage = $"Added {repo.Owner}/{repo.Name}.";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not add repository: {exception.Message}";
            return false;
        }
    }

    public void RemoveRepo(RepoConfig repo) => Repos.Remove(repo);

    /// <summary>Projects the current edits into an <see cref="AppSettings"/>.</summary>
    public AppSettings BuildSettings() => new(
        Repos: [.. Repos],
        Identity: new Identity(Login),
        RuleToggles: Rules.ToDictionary(rule => rule.RuleId, rule => rule.Enabled),
        PollIntervalSeconds: PollIntervalSeconds,
        NotificationsEnabled: NotificationsEnabled);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(BuildSettings(), cancellationToken);
            StatusMessage = "Saved.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not save: {exception.Message}";
        }
    }
}
