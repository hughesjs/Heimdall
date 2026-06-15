using System.Text.Json;

namespace Heimdall.Core.Auth;

/// <summary>
/// Raw-<see cref="HttpClient"/> implementation of GitHub's device flow against its two endpoints.
/// The client id is public (no secret); the polling delay is injectable so the loop is testable.
/// </summary>
public sealed class DeviceFlowAuthenticator : IDeviceFlowAuthenticator
{
    private const string DeviceCodeEndpoint = "https://github.com/login/device/code";
    private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _scope;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DeviceFlowAuthenticator(
        HttpClient http,
        string clientId,
        string scope = "repo",
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _clientId = clientId;
        _scope = scope;
        _delay = delay ?? ((duration, cancellationToken) => Task.Delay(duration, cancellationToken));
    }

    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        using var response = await PostAsync(DeviceCodeEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = _scope
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        return new DeviceCodeResponse(
            DeviceCode: root.GetProperty("device_code").GetString()!,
            UserCode: root.GetProperty("user_code").GetString()!,
            VerificationUri: root.GetProperty("verification_uri").GetString()!,
            ExpiresInSeconds: root.GetProperty("expires_in").GetInt32(),
            IntervalSeconds: root.GetProperty("interval").GetInt32());
    }

    public async Task<string> PollForTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(deviceCode.IntervalSeconds);
        while (true)
        {
            await _delay(interval, cancellationToken);

            using var response = await PostAsync(TokenEndpoint, new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = DeviceGrantType
            }, cancellationToken);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;

            if (root.TryGetProperty("access_token", out var accessToken))
                return accessToken.GetString()!;

            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            switch (error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    // GitHub asks us to back off; honour its new interval, or add the spec's 5 seconds.
                    interval = root.TryGetProperty("interval", out var newInterval)
                        ? TimeSpan.FromSeconds(newInterval.GetInt32())
                        : interval + TimeSpan.FromSeconds(5);
                    break;
                case "expired_token":
                    throw new DeviceFlowException(DeviceFlowError.Expired);
                case "access_denied":
                    throw new DeviceFlowException(DeviceFlowError.Denied);
                default:
                    throw new DeviceFlowException(DeviceFlowError.Unexpected, error);
            }
        }
    }

    private Task<HttpResponseMessage> PostAsync(string endpoint, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.ParseAdd("application/json");
        return _http.SendAsync(request, cancellationToken);
    }
}
