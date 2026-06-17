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

            loaded.Repos.Select(r => (r.Owner, r.Name, r.DefaultBranch)).ShouldBe(settings.Repos.Select(r => (r.Owner, r.Name, r.DefaultBranch)));
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
    public async Task Round_trips_announce_configuration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-announce-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var settings = AppSettings.Default with
            {
                Repos = [new RepoConfig("octo", "demo", "main") { AnnounceWorkflows = ["CD", "Release"], AnnounceFailures = true }]
            };

            await store.SaveAsync(settings, default);
            var loaded = await store.LoadAsync(default);

            var repo = loaded.Repos.ShouldHaveSingleItem();
            repo.AnnounceWorkflows.ShouldBe(["CD", "Release"]);
            repo.AnnounceFailures.ShouldBeTrue();
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

    [Fact]
    public async Task Save_succeeds_while_the_settings_file_is_being_read_concurrently()
    {
        // The polling loop reads settings.json continuously via LoadAsync; a concurrent SaveAsync must not
        // fail. Before LoadAsync opened the file with FileShare.Delete, the atomic rename-replace in
        // SaveAsync threw a sharing violation on Windows whenever a read overlapped it. This drives the
        // real production reader (not a hand-rolled handle) in a tight loop while hammering saves, so the
        // share mode that actually fixes the bug is the thing under test. On Windows pre-fix this fails
        // reliably; on POSIX, rename-over-open always succeeds.
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-concurrent-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            await store.SaveAsync(AppSettings.Default, default);

            var stop = false;
            var reader = Task.Run(async () =>
            {
                while (!Volatile.Read(ref stop))
                    await store.LoadAsync(default);
            });

            for (var i = 0; i < 100; i++)
                await store.SaveAsync(AppSettings.Default with { PollIntervalSeconds = i }, default);

            Volatile.Write(ref stop, true);
            await reader; // also surfaces any exception thrown on the read side

            (await store.LoadAsync(default)).PollIntervalSeconds.ShouldBe(99);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Save_surfaces_the_failure_when_the_destination_can_never_be_replaced()
    {
        // Point the store at a path that is actually a (non-empty) directory: File.Move(overwrite: true)
        // can't replace a directory with a file, so it throws on every attempt — IOException on POSIX,
        // UnauthorizedAccessException on Windows (both are what ReplaceWithRetryAsync retries on). This
        // pins its terminal contract: a genuinely stuck save exhausts its retries and propagates the error
        // rather than swallowing it (which would silently lose the user's edits).
        var directoryAsPath = Path.Combine(Path.GetTempPath(), $"heimdall-stuck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryAsPath);
        await File.WriteAllTextAsync(Path.Combine(directoryAsPath, "occupied"), "x");
        try
        {
            var store = new JsonSettingsStore(directoryAsPath);

            var exception = await Should.ThrowAsync<Exception>(() => store.SaveAsync(AppSettings.Default, default));
            (exception is IOException or UnauthorizedAccessException).ShouldBeTrue($"Unexpected exception type: {exception.GetType()}");
        }
        finally
        {
            Directory.Delete(directoryAsPath, recursive: true);
        }
    }
}
