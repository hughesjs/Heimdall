using System.Runtime.Versioning;

namespace Heimdall.Core.Auth.TokenStores;

/// <summary>Stores the token in the macOS login keychain via the <c>security</c> tool.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacosTokenStore(string service = "heimdall", string account = "github") : ITokenStore
{
    public async Task<string?> GetTokenAsync()
    {
        var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
            "security", ["find-generic-password", "-a", account, "-s", service, "-w"]);
        return exitCode == 0 && stdout.Length > 0 ? stdout.TrimEnd('\n') : null;
    }

    public async Task SaveTokenAsync(string token)
    {
        // -U updates the item if it already exists rather than failing.
        // Known limitation: `security` takes the secret as an argv element (-w <token>), so it is briefly
        // visible to other same-user processes via `ps` during this call. add-generic-password has no
        // non-interactive stdin path, so this is accepted for a local single-user app. Windows (DPAPI) and
        // Linux (secret-tool via stdin) do not have this exposure.
        var (exitCode, _, stderr) = await ProcessRunner.RunAsync(
            "security", ["add-generic-password", "-a", account, "-s", service, "-w", token, "-U"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"security add-generic-password failed: {stderr}");
    }

    public async Task ClearAsync() =>
        await ProcessRunner.RunAsync("security", ["delete-generic-password", "-a", account, "-s", service]);
}
