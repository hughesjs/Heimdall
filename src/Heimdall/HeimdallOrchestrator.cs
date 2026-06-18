using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Heimdall.Core.Auth;
using Heimdall.Core.GitHub;
using Heimdall.Core.Models;
using Heimdall.Core.Notifications;
using Heimdall.Core.Polling;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;
using Heimdall.Core.Updates;
using Heimdall.Platform;
using Heimdall.ViewModels;
using Heimdall.Views;

namespace Heimdall;

/// <summary>
/// Runtime brain of the app: owns the tray icon and menu, runs onboarding when there is no token, wires
/// the polling service's events to the tray and notifications, and manages the settings window and quit.
/// All UI mutations are marshalled to the UI thread.
/// </summary>
internal sealed class HeimdallOrchestrator(
    IClassicDesktopStyleApplicationLifetime lifetime,
    ISettingsStore settingsStore,
    ITokenStore tokenStore,
    AuthCoordinator authCoordinator,
    INotificationManager notifications) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly NativeMenu _menu = new();
    private readonly TrayIcon _trayIcon = new() { ToolTipText = "Heimdall", IsVisible = true };

    private IGitHubGateway? _gateway;
    private SettingsWindow? _settingsWindow;

    private bool _updateChecked;
    private (string TagName, string Url)? _availableUpdate;
    private IReadOnlyList<PipelineState> _pipelines = [];

    public void Start()
    {
        _trayIcon.Icon = LoadIcon(TrayStatus.Grey);
        _trayIcon.Menu = _menu;
        RebuildMenu([]);
        TrayIcon.SetIcons(Application.Current!, [_trayIcon]);
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var token = await tokenStore.GetTokenAsync() ?? await OnboardAsync();

            // Each session runs the poll loop against one token. A 401 cancels the session; we then
            // re-authenticate and start a fresh session with a new gateway, rather than spinning on the
            // revoked token (which would re-fire AuthenticationFailed every cycle).
            while (token is not null && !_cts.IsCancellationRequested)
            {
                var reauthRequired = await RunSessionAsync(token);
                if (!reauthRequired)
                    return;

                await tokenStore.ClearAsync();
                token = await OnboardAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Runs one polling session against <paramref name="token"/>. Returns true if it stopped because re-auth is needed.</summary>
    private async Task<bool> RunSessionAsync(string token)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var reauthRequired = false;

        var gateway = new GitHubGateway(GitHubClientFactory.Create(token));
        _gateway = gateway;

        if (!_updateChecked)
        {
            _updateChecked = true; // once per launch; a mid-session re-auth must not re-notify
            _ = CheckForUpdateAsync(gateway, sessionCts.Token);
        }

        var polling = new PollingService(gateway, new RelevanceEngine(StandardRules.All));
        polling.Aggregate += status => Dispatcher.UIThread.Post(() => SetStatus(status));
        polling.Snapshot += pipelines => Dispatcher.UIThread.Post(() => RebuildMenu(pipelines));
        polling.Transition += payload => Dispatcher.UIThread.Post(() =>
        {
            var (title, body) = NotificationContent.Format(payload);
            _ = notifications.ShowAsync(title, body, isAlert: payload.Kind == NotificationKind.Broke);
        });
        polling.AuthenticationFailed += () =>
        {
            reauthRequired = true;
            sessionCts.Cancel(); // stop this session; RunAsync re-auths and starts a fresh one
        };

        try
        {
            await polling.RunAsync(settingsStore, sessionCts.Token);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            // Cancelled to re-authenticate, not to shut down.
        }

        return reauthRequired && !_cts.IsCancellationRequested;
    }

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

    private async Task<string?> OnboardAsync()
    {
        var viewModel = new DeviceFlowViewModel(authCoordinator);
        var window = new DeviceFlowWindow { DataContext = viewModel };
        try
        {
            window.Show();
            return await viewModel.AuthenticateAsync(_cts.Token);
        }
        finally
        {
            window.Close();
        }
    }

    private void SetStatus(TrayStatus status)
    {
        _trayIcon.Icon = LoadIcon(status);
        _trayIcon.ToolTipText = $"Heimdall — {status}";
    }

    // The macOS tray-menu exporter binds its native menu to the first NativeMenu instance it sees and
    // only supports in-place updates of that instance — assigning a fresh NativeMenu throws
    // "The menu being updated does not match." So we keep one menu for the app's lifetime and rebuild
    // its items in place rather than replacing _trayIcon.Menu.
    private void RebuildMenu(IReadOnlyList<PipelineState> pipelines)
    {
        _pipelines = pipelines;
        _menu.Items.Clear();

        var menu = TrayMenuModel.Build(pipelines);
        if (menu.Repos.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var group in menu.Repos)
                _menu.Items.Add(new NativeMenuItem(group.Header) { Menu = SubmenuOf(group.Pipelines) });
        }

        _menu.Items.Add(new NativeMenuItemSeparator());

        // Announce-only releases live below the separator and only when there are any to show.
        if (menu.RecentlyAnnounced.Count > 0)
            _menu.Items.Add(new NativeMenuItem("Recently announced") { Menu = SubmenuOf(menu.RecentlyAnnounced) });

        if (_availableUpdate is { } update)
        {
            var updateItem = new NativeMenuItem($"⬆ Update available — {update.TagName}");
            updateItem.Click += (_, _) => Shell.OpenUrl(update.Url);
            _menu.Items.Add(updateItem);
        }

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();
        _menu.Items.Add(settings);

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        _menu.Items.Add(quit);

        static NativeMenu SubmenuOf(IReadOnlyList<TrayMenuEntry> entries)
        {
            var submenu = new NativeMenu();
            foreach (var entry in entries)
            {
                var item = new NativeMenuItem($"{entry.Dot} {entry.Label}");
                var url = entry.Url; // capture per-iteration so each item opens its own run
                item.Click += (_, _) => Shell.OpenUrl(url);
                submenu.Items.Add(item);
            }

            return submenu;
        }
    }

    private void ShowSettings()
    {
        if (_gateway is null)
            return;

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(settingsStore, _gateway, notifications);
        _settingsWindow = new SettingsWindow { DataContext = viewModel };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _ = viewModel.LoadAsync(_cts.Token);
        _settingsWindow.Show();
    }

    private void Quit()
    {
        _cts.Cancel();
        _trayIcon.IsVisible = false;
        lifetime.Shutdown();
    }

    private static WindowIcon LoadIcon(TrayStatus status)
    {
        using var stream = AssetLoader.Open(new Uri(TrayIconAssets.ResourceFor(status)));
        return new WindowIcon(stream);
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        _cts.Dispose();
        _trayIcon.Dispose();
    }
}
