[![Build CI](https://img.shields.io/github/actions/workflow/status/hughesjs/Heimdall/ci.yml?label=BUILD%20CI&style=for-the-badge)](https://github.com/hughesjs/Heimdall/actions)
[![Build CD](https://img.shields.io/github/actions/workflow/status/hughesjs/Heimdall/cd.yml?label=BUILD%20CD&style=for-the-badge&branch=master)](https://github.com/hughesjs/Heimdall/actions)
![GitHub top language](https://img.shields.io/github/languages/top/hughesjs/Heimdall?style=for-the-badge)
[![GitHub](https://img.shields.io/github/license/hughesjs/Heimdall?style=for-the-badge)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/hughesjs/Heimdall?style=for-the-badge)](https://github.com/hughesjs/Heimdall/releases/latest)
![FTB](https://raw.githubusercontent.com/hughesjs/custom-badges/master/made-in/made-in-scotland.svg)

---

# Heimdall

A cross-platform tray app that notifies a developer when the GitHub Actions runs relevant to them fail or recover, instead of having to watch every pipeline.

Built with C#/.NET 10 and [Avalonia](https://avaloniaui.net/). It is a per-developer install that polls the GitHub REST API directly, so there is no central server. See [`SPEC.md`](SPEC.md) and [`DESIGN.md`](DESIGN.md) for the full design.

## Features

- Notifies on runs you triggered, PRs you opened, and (optionally) any breakage of the default branch. Each rule can be turned off.
- Fires on state changes, green to red and red to green, rather than repeatedly while the status stays the same.
- Lets you mark a workflow, such as your CD pipeline, so its successful runs tell you a release shipped. You can also opt in to alerts when one of those runs fails.
- Shows a tray icon that sums up your pipelines: green when all is well, red when something of yours is broken, amber when a relevant run is in progress, and grey when it is not connected. Clicking a pipeline opens the run.
- Authenticates with the GitHub OAuth device flow and keeps the token in the OS keychain.
- Uses conditional ETag requests and backs off when the rate limit runs low.

## Install

Builds are on the [releases](https://github.com/hughesjs/Heimdall/releases/latest) page.

### macOS

Download the `.dmg` for your architecture (`arm64` for Apple Silicon, `x64` for Intel), open it, and drag Heimdall to Applications.

The app is unsigned, so the first launch is blocked by Gatekeeper. Either right-click Heimdall and choose Open, or run:

```bash
xattr -dr com.apple.quarantine /Applications/Heimdall.app
```

Heimdall runs in the menu bar; it has no Dock icon.

### Linux

```bash
tar -xzf Heimdall-linux-<arch>-v<version>.tar.gz   # x64 or arm64
./Heimdall
```

The tray uses `StatusNotifierItem`. GNOME requires the [AppIndicator and KStatusNotifierItem Support](https://extensions.gnome.org/extension/615/appindicator-support/) extension; most other desktops work without it. Notifications use `notify-send` from `libnotify`.

### Windows

Unzip and run `Heimdall.exe`, choosing `x64` or `arm64` to match the machine.

## First run

1. Heimdall starts the GitHub device flow: it shows a code, opens `github.com/login/device`, and you enter the code and approve. The token is saved to the OS keychain (Credential Manager/DPAPI, Keychain, or libsecret).
2. Open Settings from the tray menu and set your GitHub login, add repositories (`owner/repo`), and choose which relevance rules are enabled.
3. The tray indicator updates each poll (60 seconds by default).

## Configuration

Settings are edited in the tray's Settings window and stored as JSON under the platform's application-data folder. The auth token is not stored there; it lives in the OS keychain.

### Relevance rules

A run is relevant if any enabled rule matches:

| Rule | Matches when | Default |
|------|--------------|---------|
| Runs I triggered | the run's triggering actor is you | on |
| PRs I opened | the run is for a pull request you authored | on |
| Default branch breaking | the run is on the repository's default branch, regardless of who triggered it | off |

### Release announcements

Per repository, the Settings window lists the repo's workflows (fetched from GitHub); tick the ones to treat as announce workflows. Apart from the relevance rules, a new successful run of an announce workflow produces a notification ("`repo`: `workflow` shipped"). Enabling "Notify on failures too" also reports failed runs of those workflows. Announcements are deduplicated per run, stay silent on first sighting and after a restart (so a pre-existing release is not re-announced), and never change the tray colour on their own. Matching is by workflow name, case-insensitive, regardless of who triggered the run.

### Other settings

- Poll interval: how often Heimdall checks GitHub (default 60 seconds). Conditional requests keep unchanged data cheap against the rate limit.
- Notifications: a master toggle for desktop notifications. The tray still updates when it is off.
- Send test notification: fires a sample notification to confirm the platform notifier works.

## How it works

Heimdall tracks each pipeline line, meaning one `(repo, workflow, branch)` combination, and remembers its last settled status. On each poll it fetches recent runs and keeps the ones relevant to you, plus any announce-workflow runs. It then compares the latest run per line against the remembered status. A settled green-to-red flip notifies as broke, red-to-green as recovered, and a new success on an announce workflow as shipped. In-progress and cancelled or skipped runs do not produce notifications.

The tray icon sums up your relevant pipelines in priority order: grey, then red, then amber, then green, so a known breakage stays visible while something re-runs. There is no central server and no telemetry; Heimdall talks only to GitHub, read-only.

## Building from source

Requires the .NET 10 SDK.

```bash
dotnet build Heimdall.slnx
dotnet test  Heimdall.slnx
dotnet run --project src/Heimdall
```

The keychain-backed token-store tests are opt-in (they need a live secret service): `HEIMDALL_SECRET_TESTS=1 dotnet test Heimdall.slnx`.

### Project layout

```
src/
  Heimdall.Core/    Domain logic, no Avalonia dependency
    Auth/ GitHub/ Polling/ Rules/ Notifications/ Settings/ Models/
  Heimdall/         Avalonia app: tray, notifications, settings UI, platform implementations
  Heimdall.Tests/   xUnit and Shouldly over Core
  Heimdall.UiTests/ ViewModel tests
```

## Releasing

Pushes to `master` that touch `src/**` are versioned from conventional-commit history, tested on all three operating systems, built for six targets (Windows, macOS, and Linux on x64 and arm64), and published as a GitHub release. Pull requests are gated by CI. Commit prefixes drive the version bump: `feat`/`minor` bump the minor version, `fix`/`perf`/`refactor`/`chore`/`docs`/`ci`/`build` bump the patch, and `major`/`breaking` bump the major.

## License

MIT. See [`LICENSE`](LICENSE).
