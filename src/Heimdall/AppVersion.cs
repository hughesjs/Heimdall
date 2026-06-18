using System.Reflection;

namespace Heimdall;

/// <summary>The running build's version, read from the entry assembly (set by CI via -p:Version).</summary>
internal static class AppVersion
{
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
}
