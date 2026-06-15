using Heimdall.Core.Notifications;

namespace Heimdall.Notifications;

/// <summary>Selects the desktop notification manager for the current operating system.</summary>
internal static class NotificationManagerFactory
{
    public static INotificationManager Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsNotificationManager();
        if (OperatingSystem.IsMacOS())
            return new MacosNotificationManager();
        if (OperatingSystem.IsLinux())
            return new LinuxNotificationManager();

        throw new PlatformNotSupportedException("No notification manager is available for this operating system.");
    }
}
