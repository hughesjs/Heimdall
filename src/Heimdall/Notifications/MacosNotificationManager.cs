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
        // The body and title are passed as osascript arguments (read back as `item 1`/`item 2 of argv`)
        // rather than interpolated into the AppleScript source, so a quote or backslash in a repo/branch
        // name can't break out of the string literal. Nothing to escape.
        Shell.TryStart("osascript",
            "-e", "on run argv",
            "-e", "display notification (item 1 of argv) with title (item 2 of argv)",
            "-e", "end run",
            body, title);
        return Task.CompletedTask;
    }
}
