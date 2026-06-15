namespace Heimdall.Core.Auth.TokenStores;

/// <summary>Selects the secure token store for the current operating system.</summary>
public static class TokenStoreFactory
{
    public static ITokenStore Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsTokenStore();
        if (OperatingSystem.IsMacOS())
            return new MacosTokenStore();
        if (OperatingSystem.IsLinux())
            return new LinuxTokenStore();

        throw new PlatformNotSupportedException("No secure token store is available for this operating system.");
    }
}
