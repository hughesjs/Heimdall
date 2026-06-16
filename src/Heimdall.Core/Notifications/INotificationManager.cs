namespace Heimdall.Core.Notifications;

/// <summary>Shows a desktop notification. Platform-specific implementations.</summary>
public interface INotificationManager
{
    /// <summary>Shows a notification. <paramref name="isAlert"/> hints at error styling (e.g. a red icon).</summary>
    Task ShowAsync(string title, string body, bool isAlert = false);
}
