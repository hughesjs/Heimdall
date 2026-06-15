using Heimdall.Core.Auth;
using Heimdall.Core.Auth.TokenStores;
using Shouldly;

namespace Heimdall.Tests.Auth;

public class TokenStoreTests
{
    /// <summary>
    /// Opt-in flag for the keychain-backed stores, which need a live secret backend (GNOME Keyring /
    /// login keychain). Set <c>HEIMDALL_SECRET_TESTS=1</c> to exercise them; otherwise they skip so CI
    /// and headless runs stay green. The Windows DPAPI store needs no backend and always runs there.
    /// </summary>
    private static bool SecretBackendTestsEnabled =>
        Environment.GetEnvironmentVariable("HEIMDALL_SECRET_TESTS") == "1";

    [Fact]
    public void Factory_selects_the_store_for_the_current_os()
    {
        var store = TokenStoreFactory.Create();

        if (OperatingSystem.IsWindows())
            store.ShouldBeOfType<WindowsTokenStore>();
        else if (OperatingSystem.IsMacOS())
            store.ShouldBeOfType<MacosTokenStore>();
        else if (OperatingSystem.IsLinux())
            store.ShouldBeOfType<LinuxTokenStore>();
    }

    [SkippableFact]
    public async Task Windows_store_round_trips_via_dpapi()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");

        var path = Path.Combine(Path.GetTempPath(), $"heimdall-token-{Guid.NewGuid():N}.bin");
        try
        {
            var store = new WindowsTokenStore(path);
            (await store.GetTokenAsync()).ShouldBeNull();

            await store.SaveTokenAsync("gho_secret");
            (await store.GetTokenAsync()).ShouldBe("gho_secret");

            await store.ClearAsync();
            (await store.GetTokenAsync()).ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SkippableFact]
    public async Task Linux_store_round_trips_via_secret_service()
    {
        Skip.IfNot(OperatingSystem.IsLinux() && SecretBackendTestsEnabled, "Set HEIMDALL_SECRET_TESTS=1 on Linux to run.");
        await AssertRoundTrips(new LinuxTokenStore(service: $"heimdall-test-{Guid.NewGuid():N}"));
    }

    [SkippableFact]
    public async Task Macos_store_round_trips_via_keychain()
    {
        Skip.IfNot(OperatingSystem.IsMacOS() && SecretBackendTestsEnabled, "Set HEIMDALL_SECRET_TESTS=1 on macOS to run.");
        await AssertRoundTrips(new MacosTokenStore(service: $"heimdall-test-{Guid.NewGuid():N}"));
    }

    private static async Task AssertRoundTrips(ITokenStore store)
    {
        try
        {
            (await store.GetTokenAsync()).ShouldBeNull();
            await store.SaveTokenAsync("gho_secret");
            (await store.GetTokenAsync()).ShouldBe("gho_secret");
        }
        finally
        {
            await store.ClearAsync();
        }

        (await store.GetTokenAsync()).ShouldBeNull();
    }
}
