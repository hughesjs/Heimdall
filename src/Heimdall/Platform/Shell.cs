using System.Diagnostics;

namespace Heimdall.Platform;

/// <summary>Fire-and-forget process helpers for best-effort OS integration (notifications, opening URLs).</summary>
internal static class Shell
{
    /// <summary>Starts a process without waiting; swallows failures since these integrations are best-effort.</summary>
    public static void TryStart(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
        }
        catch
        {
            // Best-effort: a missing tool or desktop service must not crash the app.
        }
    }

    /// <summary>Opens a URL in the user's default browser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch
        {
            // Best-effort.
        }
    }
}
