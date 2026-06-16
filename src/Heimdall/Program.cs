using Avalonia;

namespace Heimdall;

internal sealed class Program
{
    // Don't touch Avalonia, third-party APIs, or SynchronizationContext-reliant code before
    // StartWithClassicDesktopLifetime runs — nothing is initialised yet.
    [STAThread]
    public static void Main(string[] args)
    {
        // Single instance: a second launch exits quietly. The lock releases on exit or crash.
        if (!SingleInstanceLock.TryAcquire(out var instanceLock))
            return;

        using (instanceLock)
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
