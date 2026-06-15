namespace Heimdall.Core.Auth;

/// <summary>Persists the GitHub access token in OS secure storage. Implementations are platform-specific.</summary>
public interface ITokenStore
{
    /// <summary>Returns the stored token, or null if none is saved.</summary>
    Task<string?> GetTokenAsync();

    Task SaveTokenAsync(string token);

    /// <summary>Removes any stored token (e.g. after revocation).</summary>
    Task ClearAsync();
}
