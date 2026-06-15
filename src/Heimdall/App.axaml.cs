using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Heimdall.Core.Auth;
using Heimdall.Core.Auth.TokenStores;
using Heimdall.Core.Settings;
using Heimdall.Notifications;

namespace Heimdall;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray-only app: no main window, so it keeps running until the user quits from the tray.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var settingsStore = new JsonSettingsStore();
            var tokenStore = TokenStoreFactory.Create();
            var authenticator = new DeviceFlowAuthenticator(new HttpClient(), GitHubOAuth.ClientId, GitHubOAuth.Scope);
            var authCoordinator = new AuthCoordinator(tokenStore, authenticator);
            var notifications = NotificationManagerFactory.Create();

            // Captured by the Exit handler, which keeps it rooted for the app lifetime and disposes it on quit.
            var orchestrator = new HeimdallOrchestrator(desktop, settingsStore, tokenStore, authCoordinator, notifications);
            orchestrator.Start();
            desktop.Exit += (_, _) => orchestrator.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
