# Update-available Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On launch, notify the user (desktop notification + persistent tray item) when a newer Heimdall release is available on GitHub than the running build.

**Architecture:** A pure version-comparison helper in `Heimdall.Core` decides "is there an update"; the existing authenticated `IGitHubGateway` gains a method to fetch the latest release; the `HeimdallOrchestrator` runs a one-shot best-effort check when the first polling session starts, then shows a notification and adds a tray menu item linking to the releases page.

**Tech Stack:** .NET 10, C#, Avalonia (tray UI), Octokit (GitHub API), xUnit + Shouldly (tests).

## Global Constraints

- Target framework: `net10.0`; nullable reference types enabled.
- Tests: xUnit with `[Theory]`/`[Fact]`, assertions via Shouldly (`.ShouldBe*`).
- Octokit types must never leak above the `IGitHubGateway` seam (keep `Octokit.Release` inside `GitHubGateway`).
- App repo coordinates are fixed: owner `hughesjs`, name `Heimdall`.
- Release tags are of the form `vX.Y.Z`; the running version is set by CI via `-p:Version`. Local/dev builds carry `1.0.0` and are NOT special-cased.
- Update notification is informational: `isAlert: false`.
- Comments explain *why*, not *what* (project convention). Full XML doc comments on new public interface members.
- Commit messages: conventional prefixes (`test:`, `feat:`); no `Co-Authored-By` trailers.
- Build/test command (run from repo root of the worktree): `dotnet test Heimdall.slnx --configuration Release`.

---

### Task 1: Pure version-comparison helper (`UpdateCheck`)

**Files:**
- Create: `src/Heimdall.Core/Updates/UpdateCheck.cs`
- Test: `src/Heimdall.Tests/Updates/UpdateCheckTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Heimdall.Core.Updates.UpdateCheck.IsUpdateAvailable(System.Version current, string latestTag) -> bool` (static).

- [ ] **Step 1: Write the failing tests**

Create `src/Heimdall.Tests/Updates/UpdateCheckTests.cs`:

```csharp
using System;
using Heimdall.Core.Updates;
using Shouldly;

namespace Heimdall.Tests.Updates;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("1.4.0", "v1.4.1", true)]        // newer patch
    [InlineData("1.4.0", "v1.5.0", true)]        // newer minor
    [InlineData("1.4.0", "v2.0.0", true)]        // newer major
    [InlineData("1.4.0", "1.5.0", true)]         // missing 'v' prefix still parses
    [InlineData("1.4.0", "v1.4.0", false)]       // equal
    [InlineData("1.4.0", "v1.3.9", false)]       // older
    [InlineData("1.4.0", "v1.5.0-rc.1", true)]   // prerelease suffix dropped
    [InlineData("1.4.0", "v1.5.0+abcdef", true)] // build metadata dropped
    [InlineData("1.4.0", "v1.4.0+abcdef", false)]// equal once metadata dropped
    [InlineData("1.4.0", "v2", true)]            // single component
    [InlineData("1.9.0", "v2.1", true)]          // two components
    [InlineData("1.4.0", "garbage", false)]      // unparseable -> no update
    [InlineData("1.4.0", "", false)]             // empty -> no update
    [InlineData("1.4.0", "v", false)]            // bare prefix -> no update
    public void Reports_update_availability(string current, string latestTag, bool expected)
    {
        UpdateCheck.IsUpdateAvailable(Version.Parse(current), latestTag).ShouldBe(expected);
    }

    [Fact]
    public void Four_part_current_equal_to_three_part_tag_is_not_an_update()
    {
        // Running assembly versions are X.Y.Z.0; the tag is vX.Y.Z. These must compare equal.
        UpdateCheck.IsUpdateAvailable(new Version(1, 4, 0, 0), "v1.4.0").ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Heimdall.Tests/Heimdall.Tests.csproj --configuration Release`
Expected: compile error / FAIL — `UpdateCheck` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Heimdall.Core/Updates/UpdateCheck.cs`:

```csharp
namespace Heimdall.Core.Updates;

/// <summary>
/// Decides whether a GitHub release tag represents a newer version than the one currently running.
/// Pure and platform-agnostic: the caller supplies the running <see cref="Version"/>.
/// </summary>
public static class UpdateCheck
{
    /// <summary>
    /// Returns true when <paramref name="latestTag"/> parses to a strictly higher version than
    /// <paramref name="current"/>. Tags may carry a leading <c>v</c> and a SemVer
    /// <c>-prerelease</c>/<c>+metadata</c> suffix, both of which are ignored. Comparison is on
    /// major.minor.patch only; an unparseable tag yields false (treated as "no update").
    /// </summary>
    public static bool IsUpdateAvailable(Version current, string latestTag)
    {
        if (!TryParseTag(latestTag, out var latest))
            return false;

        return Normalise(latest) > Normalise(current);
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
            text = text[1..];

        // Drop a SemVer prerelease (-rc.1) or build-metadata (+sha) suffix.
        var suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            text = text[..suffix];

        // System.Version needs at least major.minor; pad a single component so "2" -> "2.0".
        if (!text.Contains('.'))
            text += ".0";

        return Version.TryParse(text, out version!);
    }

    // System.Version treats unspecified components as -1, so a 3-part tag would sort below an
    // otherwise-equal 4-part assembly version. Compare only major.minor.patch, with patch floored at 0.
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Heimdall.Tests/Heimdall.Tests.csproj --configuration Release`
Expected: PASS — all `UpdateCheckTests` green.

- [ ] **Step 5: Commit**

```bash
git add src/Heimdall.Core/Updates/UpdateCheck.cs src/Heimdall.Tests/Updates/UpdateCheckTests.cs
git commit -m "feat: add version-comparison helper for update checks"
```

---

### Task 2: Fetch the latest release via the gateway

**Files:**
- Create: `src/Heimdall.Core/GitHub/ReleaseInfo.cs`
- Modify: `src/Heimdall.Core/GitHub/IGitHubGateway.cs`
- Modify: `src/Heimdall.Core/GitHub/GitHubGateway.cs`
- Modify: `src/Heimdall.Tests/TestSupport/FakeGitHubGateway.cs`
- Modify: `src/Heimdall.UiTests/Fakes.cs`

**Interfaces:**
- Consumes: nothing from prior tasks.
- Produces:
  - `Heimdall.Core.GitHub.ReleaseInfo` — `public sealed record ReleaseInfo(string TagName, string HtmlUrl)`.
  - `IGitHubGateway.GetLatestReleaseAsync(CancellationToken) -> Task<ReleaseInfo?>` (returns `null` when there is no published release).

This task has no unit test of its own: there is no mocking library in the solution, so the Octokit-backed `GetLatestReleaseAsync` mapping is verified by the compile + the manual run in Task 3. The deliverable is "the solution compiles with the new interface member implemented everywhere."

- [ ] **Step 1: Create the `ReleaseInfo` record**

Create `src/Heimdall.Core/GitHub/ReleaseInfo.cs`:

```csharp
namespace Heimdall.Core.GitHub;

/// <summary>The app's narrow view of a GitHub release: its tag and the page a user opens to download it.</summary>
public sealed record ReleaseInfo(string TagName, string HtmlUrl);
```

- [ ] **Step 2: Add the interface member**

In `src/Heimdall.Core/GitHub/IGitHubGateway.cs`, add this member inside the `IGitHubGateway` interface, after `GetAccessibleRepositoriesAsync` (before the `LastRateLimit` property):

```csharp
    /// <summary>Returns the repository's latest published release (excluding drafts/prereleases), or null if there is none.</summary>
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken);
```

- [ ] **Step 3: Implement it in `GitHubGateway`**

In `src/Heimdall.Core/GitHub/GitHubGateway.cs`:

a) Add the repo coordinates as constants next to `RecentRunsPageSize`:

```csharp
    private const int RecentRunsPageSize = 50;

    // Heimdall only ever checks itself for updates.
    private const string SelfOwner = "hughesjs";
    private const string SelfName = "Heimdall";
```

b) Add the method (place it after `GetAccessibleRepositoriesAsync`, before `MapAsync`):

```csharp
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var release = await Guard(() => _client.Repository.Release.GetLatest(SelfOwner, SelfName));
            CaptureRateLimit();
            return new ReleaseInfo(release.TagName, release.HtmlUrl);
        }
        catch (NotFoundException)
        {
            // No published release yet — not an error for our purposes.
            return null;
        }
    }
```

(`NotFoundException` is `Octokit.NotFoundException`; the file already has `using Octokit;`.)

- [ ] **Step 4: Update the Core test fake**

In `src/Heimdall.Tests/TestSupport/FakeGitHubGateway.cs`, add this member (e.g. after `GetAccessibleRepositoriesAsync`):

```csharp
    public Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<ReleaseInfo?>(null);
```

- [ ] **Step 5: Update the UiTests fake**

In `src/Heimdall.UiTests/Fakes.cs`, in the `FakeGitHubGateway` class, add the same member (after `GetAccessibleRepositoriesAsync`):

```csharp
    public Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<ReleaseInfo?>(null);
```

- [ ] **Step 6: Build + run the full suite to verify nothing broke**

Run: `dotnet test Heimdall.slnx --configuration Release`
Expected: PASS — solution compiles (both fakes satisfy the interface) and all existing tests stay green.

- [ ] **Step 7: Commit**

```bash
git add src/Heimdall.Core/GitHub/ReleaseInfo.cs src/Heimdall.Core/GitHub/IGitHubGateway.cs src/Heimdall.Core/GitHub/GitHubGateway.cs src/Heimdall.Tests/TestSupport/FakeGitHubGateway.cs src/Heimdall.UiTests/Fakes.cs
git commit -m "feat: fetch latest release through the GitHub gateway"
```

---

### Task 3: Wire the startup update check into the orchestrator

**Files:**
- Create: `src/Heimdall/AppVersion.cs`
- Modify: `src/Heimdall/HeimdallOrchestrator.cs`

**Interfaces:**
- Consumes:
  - `UpdateCheck.IsUpdateAvailable(Version, string)` (Task 1)
  - `IGitHubGateway.GetLatestReleaseAsync(CancellationToken)` returning `ReleaseInfo?` (Task 2)
- Produces: user-visible behaviour only (notification + tray item). No new public API; not unit-tested (UI-thread orchestrator follows the project's existing untested-orchestrator boundary). Verified by a manual run.

- [ ] **Step 1: Add the `AppVersion` helper**

Create `src/Heimdall/AppVersion.cs`:

```csharp
using System.Reflection;

namespace Heimdall;

/// <summary>The running build's version, read from the entry assembly (set by CI via -p:Version).</summary>
internal static class AppVersion
{
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
}
```

- [ ] **Step 2: Add fields and retain the latest pipeline snapshot**

In `src/Heimdall/HeimdallOrchestrator.cs`, add `using Heimdall.Core.Updates;` to the using block. Then add these fields alongside the existing `_gateway`/`_settingsWindow` fields:

```csharp
    private bool _updateChecked;
    private (string TagName, string Url)? _availableUpdate;
    private IReadOnlyList<PipelineState> _pipelines = [];
```

In `RebuildMenu`, capture the snapshot so non-poll rebuilds (the update check) keep the current pipeline items. Change the first line of `RebuildMenu` from:

```csharp
    private void RebuildMenu(IReadOnlyList<PipelineState> pipelines)
    {
        _menu.Items.Clear();
```

to:

```csharp
    private void RebuildMenu(IReadOnlyList<PipelineState> pipelines)
    {
        _pipelines = pipelines;
        _menu.Items.Clear();
```

- [ ] **Step 3: Add the update menu item to `RebuildMenu`**

In `RebuildMenu`, immediately after the "Recently announced" block and before the `Settings…` item (i.e. after the `if (menu.RecentlyAnnounced.Count > 0) ...` line), insert:

```csharp
        if (_availableUpdate is { } update)
        {
            var updateItem = new NativeMenuItem($"⬆ Update available — {update.TagName}");
            updateItem.Click += (_, _) => Shell.OpenUrl(update.Url);
            _menu.Items.Add(updateItem);
        }
```

- [ ] **Step 4: Fire the one-shot check when the first session starts**

In `RunSessionAsync`, just after `_gateway = gateway;`, insert:

```csharp
        if (!_updateChecked)
        {
            _updateChecked = true; // once per launch; a mid-session re-auth must not re-notify
            _ = CheckForUpdateAsync(gateway, sessionCts.Token);
        }
```

- [ ] **Step 5: Add the `CheckForUpdateAsync` method**

In `HeimdallOrchestrator`, add this method (e.g. after `RunSessionAsync`):

```csharp
    /// <summary>
    /// Best-effort, once-per-launch check for a newer release. On finding one, shows a notification and
    /// adds a persistent tray item linking to the release. Any failure (offline, rate-limited, no release)
    /// is swallowed — an update check must never disrupt the app.
    /// </summary>
    private async Task CheckForUpdateAsync(IGitHubGateway gateway, CancellationToken cancellationToken)
    {
        try
        {
            var release = await gateway.GetLatestReleaseAsync(cancellationToken);
            if (release is null || !UpdateCheck.IsUpdateAvailable(AppVersion.Current, release.TagName))
                return;

            Dispatcher.UIThread.Post(() =>
            {
                _availableUpdate = (release.TagName, release.HtmlUrl);

                var current = AppVersion.Current;
                _ = notifications.ShowAsync(
                    $"Heimdall {release.TagName} available",
                    $"You're on v{current.Major}.{current.Minor}.{current.Build} — click the tray menu to update.");

                RebuildMenu(_pipelines);
            });
        }
        catch
        {
            // Best-effort: a failed update check must never disrupt the app.
        }
    }
```

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet test Heimdall.slnx --configuration Release`
Expected: PASS — solution builds, all tests green.

- [ ] **Step 7: Manual verification**

Because the running build's version is `1.0.0` (local), the latest published release (`vX.Y.Z`) will read as newer, exercising the happy path. Run the app and confirm:

```bash
dotnet run --project src/Heimdall/Heimdall.csproj -c Release
```

- After sign-in and the first poll, a desktop notification appears: `Heimdall vX.Y.Z available` / `You're on v1.0.0 — click the tray menu to update.`
- The tray menu shows `⬆ Update available — vX.Y.Z` just above `Settings…`.
- Clicking it opens `https://github.com/hughesjs/Heimdall/releases/...` in the browser.
- The item persists across menu rebuilds (it stays after the next poll snapshot).

(If you have no network or the token flow is inconvenient, note that the happy path is covered by reasoning: `GetLatestReleaseAsync` returns the release, `IsUpdateAvailable(1.0.0, "vX.Y.Z")` is true, and the UI-thread post fires.)

- [ ] **Step 8: Commit**

```bash
git add src/Heimdall/AppVersion.cs src/Heimdall/HeimdallOrchestrator.cs
git commit -m "feat: notify on startup when a newer release is available"
```

---

## Notes for the implementer

- Do not add `Co-Authored-By` trailers to commits.
- Keep Octokit out of `IGitHubGateway` and above — only `GitHubGateway.cs` references `Octokit.Release`/`NotFoundException`.
- The `.editorconfig` enforces analyzer rules; if the build flags a style/analyzer warning on new code, fix it rather than suppressing.
