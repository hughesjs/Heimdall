namespace Heimdall.Core.Auth;

/// <summary>Why a device-flow authorisation did not complete.</summary>
public enum DeviceFlowError
{
    /// <summary>The user code expired before authorisation.</summary>
    Expired,

    /// <summary>The user declined the authorisation.</summary>
    Denied,

    /// <summary>An unrecognised error from GitHub.</summary>
    Unexpected
}

/// <summary>Thrown when the device flow ends without a token.</summary>
public sealed class DeviceFlowException(DeviceFlowError error, string? detail = null)
    : Exception(detail is null ? error.ToString() : $"{error}: {detail}")
{
    public DeviceFlowError Error { get; } = error;
}
