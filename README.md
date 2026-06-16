<div align="center">

# Heimdall

**A lightweight cross-platform tray app that tells each developer when *their* GitHub Actions runs fail or recover — without the firehose of watching every pipeline.**

[![CI](https://github.com/hughesjs/Heimdall/actions/workflows/ci.yml/badge.svg)](https://github.com/hughesjs/Heimdall/actions/workflows/ci.yml)
[![CD](https://github.com/hughesjs/Heimdall/actions/workflows/cd.yml/badge.svg)](https://github.com/hughesjs/Heimdall/actions/workflows/cd.yml)
[![Latest release](https://img.shields.io/github/v/release/hughesjs/Heimdall?sort=semver)](https://github.com/hughesjs/Heimdall/releases/latest)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%C2%B7%20macOS%20%C2%B7%20Linux-informational)](#install)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

</div>

---

## Why Heimdall?

If you watch *every* pipeline in your org you drown in noise; if you watch none, you miss your own breakages. Heimdall sits in your system tray and notifies you **only about the runs relevant to you**, and **only when their state changes** — green→red when something breaks, red→green when it recovers. No central server, no dashboard to keep open: it polls the GitHub REST API directly, per developer.

Named for the Norse watchman who sees all and sounds the horn when something's coming.

## Features

- 🎯 **Relevance, not noise** — notifies on runs *you* triggered, PRs *you* opened, and (optionally) any breakage of the default branch. Toggle each rule on/off.
- 🔔 **Transition-only alerts** — fires on green→red (*broke*) and red→green (*recovered*), never repeatedly for unchanged status.
- 🚀 **Release announcements** — mark a workflow (e.g. your CD pipeline) as an *announce workflow* and get a **"shipped"** ping every time it succeeds — *"this release has gone out"* — with an optional alert on failed releases too.
- 🚦 **At-a-glance tray indicator** — green (all good) · red (something of yours is broken) · amber (a relevant run is in progress) · grey (not connected). Click any pipeline in the menu to open the run.
- 🔐 **Secure, server-less auth** — GitHub OAuth **device flow** (no embedded secret, no localhost handler); the token lives in your OS keychain.
- ♻️ **Quota-friendly** — conditional ETag requests (304s don't count against your rate limit) and automatic back-off.
- 🖥️ **Cross-platform** — Windows, macOS and Linux, each a self-contained build.

## Install

Grab the latest build from the [**Releases**](https://github.com/hughesjs/Heimdall/releases/latest) page.

### macOS
Download the `.dmg` for your chip — **Apple Silicon → `arm64`**, **Intel → `x64`** — open it and drag **Heimdall** to Applications.

The app is currently **unsigned**, so Gatekeeper blocks the first launch. Either:
- **right-click Heimdall → Open → Open**, or
- run once: `xattr -dr com.apple.quarantine /Applications/Heimdall.app`

Heimdall then lives in the **menu bar** (no Dock icon).

### Linux
```bash
tar -xzf Heimdall-linux-<arch>-v<version>.tar.gz   # x64 or arm64
./Heimdall
```
The tray uses `StatusNotifierItem`. **GNOME** needs the [AppIndicator and KStatusNotifierItem Support](https://extensions.gnome.org/extension/615/appindicator-support/) extension to show it; KDE and most other desktops work out of the box. Notifications use `notify-send` (install `libnotify` if missing).

### Windows
Unzip and run `Heimdall.exe` (pick `x64` or `arm64` to match your machine).

> Releases are produced automatically for all six targets (Windows/macOS/Linux × x64/arm64) — see [Releasing](#releasing).

## First run

1. Heimdall starts the **GitHub device flow**: it shows a short code, opens `github.com/login/device`, and you enter the code and approve. The access token is saved to your OS secure storage (Keychain / Credential store via DPAPI / libsecret).
2. Open **Settings** from the tray menu and configure:
   - **GitHub login** — your username; the relevance rules match on it (required, or nothing is ever "yours").
   - **Repositories** — add `owner/repo`; access is validated when you add it.
   - **Relevance rules**, **announce workflows**, **poll interval**, **notifications** — see below.
3. The tray indicator goes live and updates every poll (~60s by default).

## Configuration

All settings are edited in the tray's **Settings** window and saved as JSON under your platform's app-data folder. The auth token is **not** in there — it lives in the OS keychain.

### Relevance rules
A run is relevant if **any enabled** rule matches:

| Rule | Fires when… | Default |
|------|-------------|:------:|
| **Runs I triggered** | you kicked off the run (`triggering_actor` is you) | on |
| **PRs I opened** | the run is for a pull request you authored | on |
| **Default branch breaking** | the run is on the repo's default branch — *regardless of who triggered it* | off |

### Release announcements (announce workflows)
Per repo, list one or more **workflow names** (e.g. `CD`, `Unified CD Pipeline`) to treat as *announce workflows*. Independently of the rules above, **every new successful run** of an announce workflow notifies you — *"`repo`: `workflow` shipped"*. Tick **Notify on failures too** to also be told when a release run fails. Announcements:
- are **deduped per run** (each run pings at most once) and **silent on first sighting / restart** (you won't get a "shipped" for yesterday's release when you launch the app),
- are **notification-only** — an announce-only pipeline never recolours the tray (the tray keeps meaning *"one of my relevant pipelines is broken"*),
- match by workflow name (case-insensitive), regardless of who triggered the run.

This works because typical CD pipelines run on a push to `main` and tag/release as a side-effect, so "the release shipped" is just that workflow succeeding.

### Other settings
- **Poll interval** — how often Heimdall checks GitHub (default 60s). Conditional requests mean unchanged data is nearly free against your quota.
- **Notifications** — master on/off for desktop notifications (the tray still updates).

## How it works

Heimdall tracks each **pipeline line** — a `(repo, workflow, branch)` triple — and remembers its last settled status. Each poll it fetches recent runs, keeps the ones relevant to you (plus any announce-workflow runs), and compares the latest run per line against what it remembers:

- a settled **green→red** flip → **broke** notification; **red→green** → **recovered**;
- a new success on an **announce workflow** → **shipped**;
- in-progress and cancelled/skipped runs never produce a false alert.

The tray icon aggregates *your relevant* pipelines: **grey** ≻ **red** ≻ **amber** ≻ **green**, so a known breakage stays visible even while something re-runs. Clicking a notification or a tray-menu item opens the run in your browser.

There's no central server and no telemetry — Heimdall talks only to GitHub, read-only.

## Building from source

Requires the **.NET 10 SDK**.

```bash
dotnet build Heimdall.slnx          # build
dotnet test  Heimdall.slnx          # run the test suite
dotnet run --project src/Heimdall   # launch the app
```

The optional keychain-backed token-store tests are opt-in (they need a live secret service): `HEIMDALL_SECRET_TESTS=1 dotnet test Heimdall.slnx`.

### Project layout
```
src/
  Heimdall.Core/    Domain logic — no Avalonia dependency
    Auth/ GitHub/ Polling/ Rules/ Notifications/ Settings/ Models/
  Heimdall/         Avalonia app — tray, notifications, settings UI, platform impls
  Heimdall.Tests/   xUnit + Shouldly over Core
  Heimdall.UiTests/ ViewModel POCO tests
```
See [`DESIGN.md`](DESIGN.md) for the architecture and decision log, and [`SPEC.md`](SPEC.md) for the product spec.

## Releasing

Every push to `master` that touches `src/**` is versioned from **conventional-commit** history, tested on all three OSes, built for six targets, and published as a GitHub Release automatically (see [`.github/workflows/cd.yml`](.github/workflows/cd.yml)). Pull requests are gated by CI ([`ci.yml`](.github/workflows/ci.yml)).

Commit messages therefore drive the version bump — use conventional prefixes: `feat:`/`minor:` (minor), `fix:`/`perf:`/`refactor:`/`chore:`/`docs:`/`ci:`/`build:` (patch), `major:`/`breaking:` (major).

## Contributing

1. Branch, implement with tests, open a PR — CI must be green.
2. Use **conventional commits** (they drive versioning).
3. Keep `Heimdall.Core` free of Avalonia; put logic in Core or a ViewModel, not in views.

## Roadmap

- **Commit-range relevance** (rule 4) — notify on runs containing *your* commits, matched on login + configured author emails.
- **Signed & notarised macOS builds** for a zero-friction install.
- Snooze / per-repo mute; Wayland-native tray.

Org repo auto-discovery, multi-CI providers, and release dashboards are explicitly out of scope.

## Platform support

| | Tray icon | Notifications | Token storage |
|---|---|---|---|
| **Windows** | native | toast (best-effort) | DPAPI |
| **macOS** | menu bar | `osascript` (best-effort) | Keychain |
| **Linux** | `StatusNotifierItem` (GNOME needs an extension) | `notify-send` | libsecret |

## License

[MIT](LICENSE) © 2026 James Hughes
