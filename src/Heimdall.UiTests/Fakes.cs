using Heimdall.Core.Auth;
using Heimdall.Core.GitHub;
using Heimdall.Core.Models;
using Heimdall.Core.Settings;

namespace Heimdall.UiTests;

internal sealed class InMemoryTokenStore(string? initial = null) : ITokenStore
{
    private string? _token = initial;
    public Task<string?> GetTokenAsync() => Task.FromResult(_token);
    public Task SaveTokenAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task ClearAsync() { _token = null; return Task.CompletedTask; }
}

internal sealed class ScriptedAuthenticator(string token) : IDeviceFlowAuthenticator
{
    public Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceCodeResponse("device-code", "WXYZ-1234", "https://github.com/login/device", 900, 5));

    public Task<string> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken) =>
        Task.FromResult(token);
}

internal sealed class DenyingAuthenticator : IDeviceFlowAuthenticator
{
    public Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceCodeResponse("device-code", "WXYZ-1234", "https://github.com/login/device", 900, 5));

    public Task<string> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken) =>
        throw new DeviceFlowException(DeviceFlowError.Denied);
}

internal sealed class FakeGitHubGateway : IGitHubGateway
{
    public Func<string, string, RepoConfig> OnValidate { get; set; } = (owner, name) => new RepoConfig(owner, name, "main");
    public RateLimitInfo? LastRateLimit => null;

    public Task<RepoConfig> ValidateAndDescribeAsync(string owner, string name, CancellationToken cancellationToken) =>
        Task.FromResult(OnValidate(owner, name));

    public Task<IReadOnlyList<RunRecord>> GetRecentRunsAsync(RepoConfig repo, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunRecord>>([]);
}

internal sealed class FakeSettingsStore(AppSettings? initial = null) : ISettingsStore
{
    public AppSettings Saved { get; private set; } = initial ?? AppSettings.Default;
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) { Saved = settings; return Task.CompletedTask; }
}
