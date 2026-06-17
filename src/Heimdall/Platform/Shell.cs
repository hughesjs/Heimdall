using System.Diagnostics;

namespace Heimdall.Platform;

/// <summary>Fire-and-forget process helpers for best-effort OS integration (notifications, opening URLs).</summary>
internal static class Shell
{
    /// <summary>Starts a process without waiting; swallows failures since these integrations are best-effort.</summary>
    public static void TryStart(string fileName, params string[] arguments) =>
        TryStart(fileName, arguments, environment: null);

    /// <summary>
    /// As <see cref="TryStart(string, string[])"/>, but also sets the given environment variables on the
    /// child. Used to hand untrusted text (notification title/body) to a script via the environment
    /// rather than interpolating it into the script source, so there is nothing to escape.
    /// </summary>
    public static void TryStart(string fileName, string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            if (environment is not null)
                foreach (var (key, value) in environment)
                    startInfo.Environment[key] = value;
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
