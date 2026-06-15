using Heimdall.Core.Auth;

namespace Heimdall.Tests.TestSupport;

/// <summary>An in-memory <see cref="ITokenStore"/> for coordinator tests.</summary>
internal sealed class InMemoryTokenStore(string? initial = null) : ITokenStore
{
    private string? _token = initial;

    public Task<string?> GetTokenAsync() => Task.FromResult(_token);
    public Task SaveTokenAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task ClearAsync() { _token = null; return Task.CompletedTask; }
}

/// <summary>An authenticator that returns a fixed token without any HTTP.</summary>
internal sealed class ScriptedAuthenticator(string token) : IDeviceFlowAuthenticator
{
    public Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceCodeResponse("device-code", "WXYZ-1234", "https://github.com/login/device", 900, 5));

    public Task<string> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken) =>
        Task.FromResult(token);
}

/// <summary>An authenticator that fails if used — proves a stored token short-circuits the flow.</summary>
internal sealed class ThrowingAuthenticator : IDeviceFlowAuthenticator
{
    public Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The device flow should not have been started.");

    public Task<string> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The device flow should not have been started.");
}
