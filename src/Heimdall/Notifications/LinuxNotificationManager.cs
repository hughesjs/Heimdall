using System.Runtime.Versioning;
using Heimdall.Core.Notifications;
using Heimdall.Platform;

namespace Heimdall.Notifications;

/// <summary>
/// Shows notifications via <c>notify-send</c> (the freedesktop D-Bus notification service). Informational
/// only — <c>notify-send</c> has no activation callback, so click-to-open a run is offered via the tray menu.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxNotificationManager : INotificationManager
{
    public Task ShowAsync(string title, string body, bool isAlert = false)
    {
        var icon = isAlert ? "dialog-error" : "dialog-information";
        // `--` ends notify-send's option parsing so a title/body beginning with `-` is treated as the
        // summary/body rather than a flag (which would otherwise silently swallow the notification).
        Shell.TryStart("notify-send", "--app-name=Heimdall", $"--icon={icon}", "--", title, body);
        return Task.CompletedTask;
    }
}
