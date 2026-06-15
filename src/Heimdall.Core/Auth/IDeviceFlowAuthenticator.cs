namespace Heimdall.Core.Auth;

/// <summary>
/// Drives GitHub's OAuth device flow (which Octokit does not implement): request a user code, then
/// poll until the user authorises and a token is issued.
/// </summary>
public interface IDeviceFlowAuthenticator
{
    Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken);

    /// <summary>Polls until the user authorises, returning the access token. Throws <see cref="DeviceFlowException"/> on denial/expiry.</summary>
    Task<string> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken);
}
