using Heimdall.Core.Notifications;
using Heimdall.Notifications;
using Shouldly;

namespace Heimdall.UiTests;

public class NotificationManagerTests
{
    [Fact]
    public void Factory_selects_the_manager_for_the_current_os()
    {
        var manager = NotificationManagerFactory.Create();

        if (OperatingSystem.IsWindows())
            manager.ShouldBeOfType<WindowsNotificationManager>();
        else if (OperatingSystem.IsMacOS())
            manager.ShouldBeOfType<MacosNotificationManager>();
        else if (OperatingSystem.IsLinux())
            manager.ShouldBeOfType<LinuxNotificationManager>();
        else
            manager.ShouldBeAssignableTo<INotificationManager>();
    }
}
