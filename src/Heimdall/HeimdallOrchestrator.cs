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
        _menu.Items.Clear();

        var groups = TrayMenuModel.Build(pipelines);
        if (groups.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var group in groups)
            {
                var submenu = new NativeMenu();
                foreach (var entry in group.Pipelines)
                {
                    var item = new NativeMenuItem($"{entry.Dot} {entry.Label}");
                    var url = entry.Url;
                    item.Click += (_, _) => Shell.OpenUrl(url);
                    submenu.Items.Add(item);
                }

                _menu.Items.Add(new NativeMenuItem(group.Header) { Menu = submenu });
            }
        }

        _menu.Items.Add(new NativeMenuItemSeparator());

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();
        _menu.Items.Add(settings);

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        _menu.Items.Add(quit);
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
