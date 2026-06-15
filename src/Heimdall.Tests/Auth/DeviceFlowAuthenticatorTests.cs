using System.Net;
using System.Text;
using Heimdall.Core.Auth;
using Heimdall.Tests.TestSupport;
using Shouldly;

namespace Heimdall.Tests.Auth;

public class DeviceFlowAuthenticatorTests
{
    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static DeviceFlowAuthenticator Authenticator(StubHttpMessageHandler handler, List<TimeSpan>? delays = null) =>
        new(new HttpClient(handler), "client-id", "repo", (duration, _) =>
        {
            delays?.Add(duration);
            return Task.CompletedTask;
        });

    private static DeviceCodeResponse Code(int intervalSeconds = 5) =>
        new("device-code", "WXYZ-1234", "https://github.com/login/device", 900, intervalSeconds);

    [Fact]
    public async Task Request_device_code_parses_the_response()
    {
        var handler = new StubHttpMessageHandler(_ => Json(
            """{"device_code":"DC","user_code":"WXYZ-1234","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));

        var code = await Authenticator(handler).RequestDeviceCodeAsync(default);

        code.DeviceCode.ShouldBe("DC");
        code.UserCode.ShouldBe("WXYZ-1234");
        code.VerificationUri.ShouldBe("https://github.com/login/device");
        code.IntervalSeconds.ShouldBe(5);
    }

    [Fact]
    public async Task Poll_returns_the_token_after_pending_responses()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""{"error":"authorization_pending"}"""),
            Json("""{"error":"authorization_pending"}"""),
            Json("""{"access_token":"gho_abc","token_type":"bearer","scope":"repo"}""")
        ]);
        var handler = new StubHttpMessageHandler(_ => responses.Dequeue());

        var token = await Authenticator(handler).PollForTokenAsync(Code(), default);

        token.ShouldBe("gho_abc");
        handler.CallCount.ShouldBe(3);
    }

    [Fact]
    public async Task Slow_down_increases_the_polling_interval()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""{"error":"slow_down","interval":10}"""),
            Json("""{"access_token":"gho_abc"}""")
        ]);
        var delays = new List<TimeSpan>();
        var handler = new StubHttpMessageHandler(_ => responses.Dequeue());

        await Authenticator(handler, delays).PollForTokenAsync(Code(intervalSeconds: 5), default);

        delays[0].ShouldBe(TimeSpan.FromSeconds(5));  // initial interval
        delays[1].ShouldBe(TimeSpan.FromSeconds(10)); // bumped after slow_down
    }

    [Fact]
    public async Task Access_denied_throws_denied()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"error":"access_denied"}"""));

        var exception = await Should.ThrowAsync<DeviceFlowException>(
            () => Authenticator(handler).PollForTokenAsync(Code(), default));
        exception.Error.ShouldBe(DeviceFlowError.Denied);
    }

    [Fact]
    public async Task Expired_token_throws_expired()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"error":"expired_token"}"""));

        var exception = await Should.ThrowAsync<DeviceFlowException>(
            () => Authenticator(handler).PollForTokenAsync(Code(), default));
        exception.Error.ShouldBe(DeviceFlowError.Expired);
    }
}
