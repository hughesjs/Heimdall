using Heimdall.Core.Models;
using Heimdall.Core.Rules;

namespace Heimdall.Core.Settings;

/// <summary>
/// Persisted user configuration. The auth token is NOT here — it lives in OS secure storage.
/// </summary>
public record AppSettings(
    IReadOnlyList<RepoConfig> Repos,
    Identity Identity,
    IReadOnlyDictionary<string, bool> RuleToggles,
    int PollIntervalSeconds,
    bool NotificationsEnabled)
{
    public const int DefaultPollIntervalSeconds = 60;

    /// <summary>First-run defaults: no repos, blank identity, MVP rule toggles, 60s polling, notifications on.</summary>
    public static AppSettings Default { get; } = new(
        Repos: [],
        Identity: new Identity(string.Empty),
        RuleToggles: new RelevanceEngine(StandardRules.All).DefaultToggles(),
        PollIntervalSeconds: DefaultPollIntervalSeconds,
        NotificationsEnabled: true);
}
