using Heimdall.Core.Models;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;
using Shouldly;

namespace Heimdall.Tests.Settings;

public class JsonSettingsStoreTests
{
    [Fact]
    public async Task Round_trips_settings_through_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var settings = new AppSettings(
                Repos: [new RepoConfig("octo", "demo", "main"), new RepoConfig("acme", "app", "trunk")],
                Identity: new Identity("alice"),
                RuleToggles: new Dictionary<string, bool>
                {
                    [TriggeredByMeRule.RuleId] = true,
                    [DefaultBranchBreakingRule.RuleId] = false
                },
                PollIntervalSeconds: 30,
                NotificationsEnabled: false);

            await store.SaveAsync(settings, default);
            var loaded = await store.LoadAsync(default);

            loaded.Repos.ShouldBe(settings.Repos);
            loaded.Identity.ShouldBe(settings.Identity);
            loaded.RuleToggles[TriggeredByMeRule.RuleId].ShouldBeTrue();
            loaded.RuleToggles[DefaultBranchBreakingRule.RuleId].ShouldBeFalse();
            loaded.PollIntervalSeconds.ShouldBe(30);
            loaded.NotificationsEnabled.ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_file_returns_defaults()
    {
        var store = new JsonSettingsStore(Path.Combine(Path.GetTempPath(), $"heimdall-missing-{Guid.NewGuid():N}.json"));

        (await store.LoadAsync(default)).ShouldBe(AppSettings.Default);
    }

    [Fact]
    public async Task Save_creates_the_directory_if_absent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"heimdall-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "settings.json");
        try
        {
            await new JsonSettingsStore(path).SaveAsync(AppSettings.Default, default);
            File.Exists(path).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_overwrites_existing_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-overwrite-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            await store.SaveAsync(AppSettings.Default with { PollIntervalSeconds = 10 }, default);
            await store.SaveAsync(AppSettings.Default with { PollIntervalSeconds = 99 }, default);

            (await store.LoadAsync(default)).PollIntervalSeconds.ShouldBe(99);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
