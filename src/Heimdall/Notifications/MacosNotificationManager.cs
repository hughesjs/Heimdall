using System.Runtime.Versioning;
using Heimdall.Core.Notifications;
using Heimdall.Platform;

namespace Heimdall.Notifications;

/// <summary>
/// Shows notifications via <c>osascript</c> (<c>display notification</c>). Best-effort and unverified in
/// CI; needs manual verification on macOS. Click-to-open a run is offered via the tray menu.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacosNotificationManager : INotificationManager
{
    public Task ShowAsync(string title, string body, bool isAlert = false)
    {
        var script = $"display notification \"{Escape(body)}\" with title \"{Escape(title)}\"";
        Shell.TryStart("osascript", "-e", script);
        return Task.CompletedTask;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
