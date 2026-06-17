using System.Runtime.Versioning;
using Heimdall.Core.Notifications;
using Heimdall.Platform;

namespace Heimdall.Notifications;

/// <summary>
/// Shows a toast via a PowerShell script that drives the Windows.UI.Notifications API. Best-effort and
/// unverified in CI; needs manual verification on Windows. Click-to-open a run is offered via the tray menu.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNotificationManager : INotificationManager
{
    // The title and body are passed through the environment ($env:*) rather than interpolated into the
    // script, so a quote or apostrophe in a repo/branch name can't break out of the PowerShell string
    // (or inject code). That makes this a constant with nothing to escape.
    private const string Script =
        "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] | Out-Null;" +
        "$t=[Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);" +
        "$t.GetElementsByTagName('text')[0].AppendChild($t.CreateTextNode($env:HEIMDALL_TOAST_TITLE)) | Out-Null;" +
        "$t.GetElementsByTagName('text')[1].AppendChild($t.CreateTextNode($env:HEIMDALL_TOAST_BODY)) | Out-Null;" +
        "$n=[Windows.UI.Notifications.ToastNotification]::new($t);" +
        "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Heimdall').Show($n);";

    public Task ShowAsync(string title, string body, bool isAlert = false)
    {
        Shell.TryStart("powershell", ["-NoProfile", "-Command", Script], new Dictionary<string, string>
        {
            ["HEIMDALL_TOAST_TITLE"] = title,
            ["HEIMDALL_TOAST_BODY"] = body
        });
        return Task.CompletedTask;
    }
}
