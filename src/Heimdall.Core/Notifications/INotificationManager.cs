using Heimdall.Core.Models;

namespace Heimdall.Core.Notifications;

/// <summary>Shows a desktop notification for a pipeline transition. Platform-specific implementations.</summary>
public interface INotificationManager
{
    /// <summary>Shows the notification. Clicking it should open the run (best-effort, platform-dependent).</summary>
    Task ShowAsync(NotificationPayload payload);
}
