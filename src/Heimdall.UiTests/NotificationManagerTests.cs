using Heimdall.Core.Notifications;
using Heimdall.Notifications;
using Shouldly;

namespace Heimdall.UiTests;

public class NotificationManagerTests
{
    [Fact]
    public void Factory_selects_the_manager_for_the_current_os()
    {
        // Create() throws on an unsupported OS, so assert the throw there rather than a returned instance.
        if (OperatingSystem.IsWindows())
            NotificationManagerFactory.Create().ShouldBeOfType<WindowsNotificationManager>();
        else if (OperatingSystem.IsMacOS())
            NotificationManagerFactory.Create().ShouldBeOfType<MacosNotificationManager>();
        else if (OperatingSystem.IsLinux())
            NotificationManagerFactory.Create().ShouldBeOfType<LinuxNotificationManager>();
        else
            Should.Throw<PlatformNotSupportedException>(() => NotificationManagerFactory.Create());
    }
}
