namespace Heimdall.Core.Auth;

/// <summary>
/// The result of starting the device flow: the code the user types at <see cref="VerificationUri"/>,
/// and the polling parameters for collecting the token.
/// </summary>
public record DeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds);
