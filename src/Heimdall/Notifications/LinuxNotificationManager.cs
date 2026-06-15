using System.Runtime.Versioning;
using Heimdall.Core.Notifications;
using Heimdall.Core.Models;
using Heimdall.Platform;

namespace Heimdall.Notifications;

/// <summary>
/// Shows notifications via <c>notify-send</c> (the freedesktop D-Bus notification service). Informational
/// only — <c>notify-send</c> has no activation callback, so click-to-open a run is offered via the tray menu.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxNotificationManager : INotificationManager
{
    public Task ShowAsync(NotificationPayload payload)
    {
        var (title, body) = NotificationContent.Format(payload);
        var icon = payload.Kind == TransitionKind.Broke ? "dialog-error" : "dialog-information";
        Shell.TryStart("notify-send", "--app-name=Heimdall", $"--icon={icon}", title, body);
        return Task.CompletedTask;
    }
}
