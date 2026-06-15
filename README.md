# Heimdall

A lightweight cross-platform **tray app** that notifies each developer when the **GitHub Actions runs relevant to them** fail or recover — without the firehose of watching every pipeline.

Built with C#/.NET 10 and [Avalonia](https://avaloniaui.net/). Per-developer install; polls the GitHub REST API directly (no central server). See [`SPEC.md`](SPEC.md) and [`DESIGN.md`](DESIGN.md) for the full design.

## Install

Grab the latest build from the [**Releases**](https://github.com/hughesjs/Heimdall/releases) page.

### macOS
Download the `.dmg` for your chip — **Apple Silicon → `arm64`**, **Intel → `x64`** — open it, and drag **Heimdall** to Applications.

The app is currently **unsigned**, so Gatekeeper blocks the first launch. Either:
- **right-click Heimdall → Open → Open**, or
- run once: `xattr -dr com.apple.quarantine /Applications/Heimdall.app`

Heimdall runs in the **menu bar** (no Dock icon).

### Linux
```bash
tar -xzf Heimdall-linux-<arch>-v<version>.tar.gz
./Heimdall
```
The tray icon uses `StatusNotifierItem`. **GNOME** doesn't support this without an extension — install [AppIndicator and KStatusNotifierItem Support](https://extensions.gnome.org/extension/615/appindicator-support/). KDE and most other desktops work out of the box. Notifications use `notify-send` (`libnotify`).

### Windows
Unzip and run `Heimdall.exe`. Pick `x64` or `arm64` to match your machine.

## First run
1. Heimdall prompts you to authorise via **GitHub device flow** — it shows a code, opens `github.com/login/device`, you enter the code and approve. The token is stored in your OS keychain.
2. Open **Settings** from the tray menu: set your GitHub login, add repositories (`owner/repo`), and choose which relevance rules are on.
3. The tray indicator aggregates *your* pipelines: **green** all good · **red** something of yours is broken · **amber** a relevant run is in progress · **grey** not connected. Click a pipeline in the menu to open the run.

## Building from source
```bash
dotnet build Heimdall.slnx
dotnet test Heimdall.slnx
dotnet run --project src/Heimdall
```
Requires the .NET 10 SDK.

## Releases
Pushes to `master` are versioned from conventional-commit history and published automatically with per-platform binaries (see [`.github/workflows/cd.yml`](.github/workflows/cd.yml)).
