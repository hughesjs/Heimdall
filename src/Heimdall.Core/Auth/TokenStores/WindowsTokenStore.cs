using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Heimdall.Core.Auth.TokenStores;

/// <summary>
/// Stores the token in a per-user file encrypted with DPAPI (<see cref="ProtectedData"/>), scoped to
/// the current Windows user so only they can decrypt it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTokenStore : ITokenStore
{
    private readonly string _path;

    public WindowsTokenStore(string? path = null) => _path = path ?? DefaultPath();

    private static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Heimdall", "token.bin");

    public Task<string?> GetTokenAsync()
    {
        if (!File.Exists(_path))
            return Task.FromResult<string?>(null);

        var protectedBytes = File.ReadAllBytes(_path);
        var plain = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
    }

    public Task SaveTokenAsync(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (File.Exists(_path))
            File.Delete(_path);
        return Task.CompletedTask;
    }
}
