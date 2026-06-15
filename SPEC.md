# Heimdall — Spec

## 1. Purpose
A lightweight cross-platform desktop tray app that notifies each developer when **GitHub Actions runs relevant to them** fail or recover — avoiding the firehose of watching every pipeline.

## 2. Platforms & stack
- Windows, macOS, Linux.
- C#/.NET 10, Avalonia v12.
- Per-dev install; polls the GitHub REST API via Octokit.NET (no central server).

## 3. Authentication
- **GitHub OAuth App**, via **device flow** (no embedded secret, no localhost handler). ~50 lines of raw `HttpClient` against two GitHub endpoints — Octokit.NET does not implement device flow natively.
- Scopes: read-only — `repo` (or fine-grained: contents:read, actions:read, pull_requests:read) sufficient for private repos.
- Token persisted in OS secure storage via `ITokenStore` (platform implementations: Windows Credential Manager / DPAPI, macOS Keychain, Linux `libsecret`).
- **Non-expiring token, no refresh.** Refreshing an OAuth App token requires the `client_secret`, which would violate "no embedded secret / no central server". So the OAuth App is configured with **expiring user tokens OFF** — the device-flow token lives until revoked. The only recovery path is **re-auth on 401** (revocation). `client_id` is not a secret and ships in the binary.

## 4. Repositories
- **Manual repo list**, entered in settings (`owner/repo`).
- Validate access on add via `client.Repository.Get(owner, repo)`. (Org auto-discovery = v2.)

## 5. Relevance rules (per-dev configurable toggles)
A run notifies if it matches **any enabled** rule.

**MVP rules** (direct field comparisons — no extra API calls):
1. **Runs I triggered** (`triggering_actor` == me).
2. **PRs I opened** (run is for a PR I authored).
3. **Default branch breaking** (run on `main`/default branch — notify regardless of authorship). *Off by default.*

**Fast-follow (v1.1):**
4. **Runs containing my commits** (my commit in the run's push range — matched on my GitHub login *and* my configured commit author emails). Requires Compare-API commit-range enumeration + author/email matching.

Identity config: GitHub login (MVP). Commit author email(s) added with rule 4 in the fast-follow.

## 6. Notification behaviour
- **Fire only on state transition**: green→red (broke) and red→green (recovered). No repeat notifications for unchanged status.
- Per-relevant-pipeline state tracked locally to detect transitions.
- **Release announcements.** Each repo may designate **announce workflows** (by name). Independently of the green↔red transition rules, a *new settled run* of an announce workflow notifies: **success → "shipped"** always; **failure → "broke"** only if the repo's *announce failures* option is on. Announcements are deduped per run (each run notifies at most once) and are **silent on first sighting / restart** (a pre-existing successful release is not re-announced). Announce workflows are notification-only: an announce-only pipeline does **not** affect the tray colour. Matching is by workflow name (case-insensitive), authorship-agnostic.
- Notification content: repo, workflow name, branch/PR, result, who triggered. **Click → open the run in the browser.**
- Notifications via `INotificationManager` (platform implementations: WinRT Toast on Windows, `UNUserNotificationCenter` on macOS, D-Bus `org.freedesktop.Notifications` on Linux).

## 7. Tray indicator
- Aggregate of *my* relevant pipelines:
  - **Green** — all good.
  - **Red** — at least one of mine is broken.
  - **Amber/spinner** — relevant run(s) in progress (optional for MVP).
  - **Grey** — not connected / error.
- Tray menu: current statuses list (click an item → open run), settings, quit.
- Tray icon via Avalonia's built-in `TrayIcon`. Note: GNOME does not support `StatusNotifierItem` without a user-installed extension (AppIndicator/Status Tray) — document for Linux users.

## 8. Settings
Auth status, repo list, identity (login; +emails with rule 4), relevance toggles, poll interval, launch-on-login (optional), notification on/off. Per repo: **announce workflows** (comma-separated workflow names) and an **announce failures** toggle (notify on failing announce-workflow runs too, not just successes).

- Settings persisted as JSON via `ISettingsStore` in Core, using `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` for cross-platform path resolution (`%APPDATA%` / `~/Library/Application Support` / `~/.config`).

## 9. Polling & rate limits
- Configurable interval (default ~60s), driven by `System.Threading.PeriodicTimer`.
- **Conditional requests (ETag/If-None-Match)** via a delegating `HttpMessageHandler` wrapping Octokit's HTTP client — unchanged data returns 304 and does not count against quota (5,000 req/hr authenticated).
- Rate limit remaining exposed via `client.GetLastApiInfo().RateLimit`; back off if low.

## 10. Single instance
- Enforced via an OS-level `FileStream` lock (`FileShare.None`) held for the process lifetime. Released automatically on crash or clean exit — no stale lock on next launch.

## 11. Distribution
- Self-contained single-file binary published per platform.
- Launch on login is out of scope for the app itself — document that Linux/macOS users can add a systemd user unit or LaunchAgent, and Windows users can add a registry run key.

## 12. Project structure
```
Heimdall.sln
src/
  Heimdall.Core/        Domain logic — no Avalonia dependency
    Auth/               ITokenStore, device flow coordinator
    GitHub/             Octokit wrapper, ETag delegating handler
    Polling/            PeriodicTimer poll loop, state machine
    Rules/              IRelevanceRule, concrete implementations
    Notifications/      INotificationManager, NotificationPayload
    Settings/           AppSettings record, ISettingsStore + JSON implementation
    Models/             Records — RunRecord, RepoConfig, PipelineState, etc.
  Heimdall/             Avalonia app — wires platform implementations
    Views/
    ViewModels/
    Auth/               Platform ITokenStore implementations
    Notifications/      Platform INotificationManager implementations
    Assets/             Tray icons (green, red, amber, grey)
    App.axaml           TrayIcon definition
  Heimdall.Tests/       xunit + Shouldly, mirroring Core structure
.editorconfig
.gitignore
```

## 13. Explicitly out of scope
PR-review/issue "action centre", multi-CI providers, release dashboards.

## 14. Parked for v2
Org repo auto-discovery, snooze/DND/per-repo mute, auto-update, Wayland-native tray (currently via XWayland).
