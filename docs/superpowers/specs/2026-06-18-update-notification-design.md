# Update-available notification — design

## Goal

On launch, Heimdall checks its own latest GitHub release. If that release is newer
than the running build, it shows a one-off desktop notification and adds a persistent
"Update available" item to the tray menu that opens the releases page.

The running version is baked in by CI via MSBuild `-p:Version=<semver>` (computed from
conventional commits), and releases are tagged `vX.Y.Z`. Local/dev builds carry the
default `1.0.0` and are intentionally **not** special-cased — they will simply read as
behind the latest release.

## Components

### 1. Fetching the latest release (Heimdall.Core)

- New record `ReleaseInfo(string TagName, string HtmlUrl)` in `Heimdall.Core.GitHub`.
- Extend `IGitHubGateway` with:
  ```csharp
  Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken);
  ```
- `GitHubGateway` implements it via `_client.Repository.Release.GetLatest("hughesjs", "Heimdall")`,
  wrapped in the existing `Guard(...)` helper and following the `CaptureRateLimit()` pattern.
  `GetLatest` already excludes drafts and prereleases.
- The owner/name are private constants on the gateway — the app only ever checks itself.
- Returns `null` when there is no published release. Access failures translated by `Guard`
  into `GitHubAccessException` are caught by the caller (the orchestrator), not here — the
  gateway keeps its existing exception contract.

### 2. Version comparison (Heimdall.Core, pure + unit-tested)

- New static class `Heimdall.Core.Updates.UpdateCheck` with a pure method:
  ```csharp
  static bool IsUpdateAvailable(Version current, string latestTag);
  ```
- Parsing rules for `latestTag`:
  - Strip a single leading `v`/`V` if present.
  - Drop any `-prerelease` or `+metadata` suffix.
  - Parse the remaining `X[.Y[.Z]]` with `Version.TryParse`. Unparseable → return `false`
    (treat as "no update" rather than throwing).
- Comparison is on `Major.Minor.Build` only. Both sides are normalised with
  `new Version(v.Major, v.Minor, Math.Max(v.Build, 0))` before comparing, to sidestep
  `System.Version`'s unspecified-component `-1` sentinel so that equal versions never read
  as "newer" (e.g. tag `v1.4.0` vs running `1.4.0.0`).
- Returns `true` only when the normalised latest is strictly greater than the normalised
  current.

### 3. Reading the running version (Heimdall app project)

- A small helper `AppVersion.Current` returns
  `Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0)`.
- Lives in the app project (the assembly that knows its own version), keeping Core's
  comparison logic pure and free of reflection on the entry assembly.

### 4. Orchestration (HeimdallOrchestrator)

- A one-shot, best-effort update check runs **once per app launch**. It is gated by a
  `_updateChecked` flag so that a mid-session re-authentication does not re-trigger it.
- Triggered when the first polling session starts (the gateway exists by then, after
  onboarding). Concretely: at the top of `RunSessionAsync`, after the gateway is created,
  if `!_updateChecked` then set the flag and fire the check (fire-and-forget, fully
  guarded in try/catch).
- The check:
  1. `var release = await gateway.GetLatestReleaseAsync(ct);` — `null` → done.
  2. `if (UpdateCheck.IsUpdateAvailable(AppVersion.Current, release.TagName))` then store
     `_availableUpdate = (release.TagName, release.HtmlUrl)` and post to the UI thread to:
     - show one notification, and
     - rebuild the menu.
- Notification content:
  - title: `Heimdall {TagName} available`
  - body: `You're on v{current} — click the tray menu to update.`
  - `isAlert: false` (informational, not an error).

### 5. Tray menu integration (RebuildMenu)

- `RebuildMenu` gains a conditional item, placed **below the existing separator and above
  `Settings…`**, shown only when `_availableUpdate` is set:
  - label: `⬆ Update available — {TagName}`
  - click: `Shell.OpenUrl(_availableUpdate.Url)` (the release's `HtmlUrl`).
- Because `RebuildMenu` is also called on every poll snapshot, reading `_availableUpdate`
  inside it means the item naturally persists across menu rebuilds once an update is found.

### 6. Failure / offline handling

- Entirely best-effort. Offline, rate-limited, `GitHubAccessException`, or no-releases-yet
  all resolve to "no update": no notification, no menu item, no crash, nothing surfaced to
  the user. The `_updateChecked` flag is set before the check runs, so a transient failure
  is not retried within the same launch — this is a once-per-launch check by design.

## Testing

- **`UpdateCheck` unit tests (Heimdall.Tests):**
  - newer patch / minor / major → `true`
  - equal version (incl. `v1.4.0` vs `1.4.0.0`) → `false`
  - older latest → `false`
  - `v` prefix and no prefix both parse
  - prerelease suffix (`v1.5.0-rc.1`) and build metadata (`v1.5.0+abc`) parse to `1.5.0`
  - malformed / empty / non-numeric tag → `false`
  - missing components (`v2`, `v2.1`) parse and compare correctly
- **Gateway mapping:** if the existing `IGitHubClient` fakes support `Repository.Release`,
  add a test that `GetLatestReleaseAsync` maps `TagName`/`HtmlUrl` and returns `null` when
  there is no release. Otherwise this path is left to manual verification, as the core
  decision logic is fully covered by the `UpdateCheck` tests.
- The notification and tray-item wiring live in the UI-thread `HeimdallOrchestrator`, which
  follows the project's existing untested-orchestrator boundary.

## Out of scope (YAGNI)

- Periodic re-checks while running (startup-only, per the request).
- Auto-download or in-app update installation (link to releases page only).
- Click-to-open on the notification itself (the tray item provides the actionable affordance;
  `INotificationManager` stays click-less).
- A "you're up to date" notification.
