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
    public Task ShowAsync(string title, string body, bool isAlert = false)
    {
        var script =
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] | Out-Null;" +
            "$t=[Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);" +
            $"$t.GetElementsByTagName('text')[0].AppendChild($t.CreateTextNode('{Escape(title)}')) | Out-Null;" +
            $"$t.GetElementsByTagName('text')[1].AppendChild($t.CreateTextNode('{Escape(body)}')) | Out-Null;" +
            "$n=[Windows.UI.Notifications.ToastNotification]::new($t);" +
            "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Heimdall').Show($n);";
        Shell.TryStart("powershell", "-NoProfile", "-Command", script);
        return Task.CompletedTask;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
