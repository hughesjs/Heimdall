using System.Runtime.Versioning;

namespace Heimdall.Core.Auth.TokenStores;

/// <summary>Stores the token in the Linux Secret Service (GNOME Keyring / KWallet) via <c>secret-tool</c>.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxTokenStore(string service = "heimdall", string account = "github") : ITokenStore
{
    private string[] Attributes() => ["service", service, "account", account];

    public async Task<string?> GetTokenAsync()
    {
        var (exitCode, stdout, _) = await ProcessRunner.RunAsync("secret-tool", ["lookup", .. Attributes()]);
        return exitCode == 0 && stdout.Length > 0 ? stdout.TrimEnd('\n') : null;
    }

    public async Task SaveTokenAsync(string token)
    {
        var (exitCode, _, stderr) = await ProcessRunner.RunAsync(
            "secret-tool", ["store", "--label=Heimdall GitHub token", .. Attributes()], stdin: token);
        if (exitCode != 0)
            throw new InvalidOperationException($"secret-tool store failed: {stderr}");
    }

    public async Task ClearAsync() => await ProcessRunner.RunAsync("secret-tool", ["clear", .. Attributes()]);
}
