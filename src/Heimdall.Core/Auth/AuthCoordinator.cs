namespace Heimdall.Core.Auth;

/// <summary>
/// Coordinates obtaining a token: returns a stored one, or runs the device flow (surfacing the user
/// code to the UI via a callback) and persists the result. Re-auth clears then re-runs the flow.
/// </summary>
public sealed class AuthCoordinator(ITokenStore tokenStore, IDeviceFlowAuthenticator authenticator)
{
    /// <summary>Returns the stored token, or runs the device flow if none is saved.</summary>
    public async Task<string> GetOrAuthenticateAsync(Func<DeviceCodeResponse, Task> onCodeReady, CancellationToken cancellationToken)
    {
        var existing = await tokenStore.GetTokenAsync();
        return existing ?? await AuthenticateAsync(onCodeReady, cancellationToken);
    }

    /// <summary>Runs the device flow start-to-finish and stores the resulting token.</summary>
    public async Task<string> AuthenticateAsync(Func<DeviceCodeResponse, Task> onCodeReady, CancellationToken cancellationToken)
    {
        var deviceCode = await authenticator.RequestDeviceCodeAsync(cancellationToken);
        await onCodeReady(deviceCode);
        var token = await authenticator.PollForTokenAsync(deviceCode, cancellationToken);
        await tokenStore.SaveTokenAsync(token);
        return token;
    }

    /// <summary>Clears the revoked token and authenticates afresh (the 401 recovery path).</summary>
    public async Task<string> ReauthenticateAsync(Func<DeviceCodeResponse, Task> onCodeReady, CancellationToken cancellationToken)
    {
        await tokenStore.ClearAsync();
        return await AuthenticateAsync(onCodeReady, cancellationToken);
    }
}
