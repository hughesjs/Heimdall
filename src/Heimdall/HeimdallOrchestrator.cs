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
    private readonly TrayIcon _trayIcon = new() { ToolTipText = "Heimdall", IsVisible = true };

    private IGitHubGateway? _gateway;
    private SettingsWindow? _settingsWindow;

    public void Start()
    {
        _trayIcon.Icon = LoadIcon(TrayStatus.Grey);
        RebuildMenu([]);
        TrayIcon.SetIcons(Application.Current!, [_trayIcon]);
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var token = await tokenStore.GetTokenAsync() ?? await OnboardAsync();
            if (token is null)
                return;

            var gateway = new GitHubGateway(GitHubClientFactory.Create(token));
            _gateway = gateway;

            var polling = new PollingService(gateway, new RelevanceEngine(StandardRules.All));
            polling.Aggregate += status => Dispatcher.UIThread.Post(() => SetStatus(status));
            polling.Snapshot += pipelines => Dispatcher.UIThread.Post(() => RebuildMenu(pipelines));
            polling.Transition += payload => Dispatcher.UIThread.Post(() => _ = notifications.ShowAsync(payload));
            polling.AuthenticationFailed += () => Dispatcher.UIThread.Post(() => _ = ReauthenticateAsync());

            await polling.RunAsync(settingsStore, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task<string?> OnboardAsync()
    {
        var viewModel = new DeviceFlowViewModel(authCoordinator);
        var window = new DeviceFlowWindow { DataContext = viewModel };
        window.Show();
        var token = await viewModel.AuthenticateAsync(_cts.Token);
        window.Close();
        return token;
    }

    private async Task ReauthenticateAsync()
    {
        var token = await authCoordinator.ReauthenticateAsync(_ => Task.CompletedTask, _cts.Token).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(token))
            _gateway = new GitHubGateway(GitHubClientFactory.Create(token));
    }

    private void SetStatus(TrayStatus status)
    {
        _trayIcon.Icon = LoadIcon(status);
        _trayIcon.ToolTipText = $"Heimdall — {status}";
    }

    private void RebuildMenu(IReadOnlyList<PipelineState> pipelines)
    {
        var menu = new NativeMenu();

        if (pipelines.Count == 0)
        {
            menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var pipeline in pipelines.OrderBy(p => p.Key.Repo).ThenBy(p => p.Key.HeadBranch))
            {
                var label = $"{pipeline.Key.Owner}/{pipeline.Key.Repo} · {pipeline.LastRun.WorkflowName} · {pipeline.Key.HeadBranch} — {Describe(pipeline)}";
                var item = new NativeMenuItem(label);
                var url = pipeline.LastRun.HtmlUrl;
                item.Click += (_, _) => Shell.OpenUrl(url);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new NativeMenuItemSeparator());

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);

        _trayIcon.Menu = menu;
    }

    private static string Describe(PipelineState pipeline) => pipeline switch
    {
        { InProgress: true } => "running",
        { LastSettledStatus: RunStatus.Failure } => "failing",
        { LastSettledStatus: RunStatus.Success } => "passing",
        _ => "unknown"
    };

    private void ShowSettings()
    {
        if (_gateway is null)
            return;

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(settingsStore, _gateway);
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
