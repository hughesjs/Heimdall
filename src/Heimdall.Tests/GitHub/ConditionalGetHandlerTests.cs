using System.Net;
using System.Net.Http.Headers;
using Heimdall.Core.GitHub;
using Heimdall.Tests.TestSupport;
using Shouldly;

namespace Heimdall.Tests.GitHub;

public class ConditionalGetHandlerTests
{
    private const string Uri = "https://api.github.com/repos/octo/demo/actions/runs";

    /// <summary>Serves a 200 with ETag "v1" + body, or a 304 when the matching If-None-Match arrives.</summary>
    private static StubHttpMessageHandler ETagStub(string body = "payload-v1") => new(request =>
    {
        if (request.Headers.IfNoneMatch.FirstOrDefault()?.Tag == "\"v1\"")
        {
            var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
            notModified.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4999");
            return notModified;
        }

        var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        ok.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        return ok;
    });

    private static HttpRequestMessage Get() => new(HttpMethod.Get, Uri);

    [Fact]
    public async Task First_get_passes_through_without_a_conditional_header_and_returns_the_body()
    {
        var stub = ETagStub();
        using var invoker = new HttpMessageInvoker(new ConditionalGetHandler(stub));

        var response = await invoker.SendAsync(Get(), default);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("payload-v1");
        stub.IfNoneMatchSeen[0].ShouldBeNull();
    }

    [Fact]
    public async Task Repeat_get_sends_if_none_match_and_replays_the_cached_body_as_200_on_304()
    {
        var stub = ETagStub();
        using var invoker = new HttpMessageInvoker(new ConditionalGetHandler(stub));

        await invoker.SendAsync(Get(), default);
        var second = await invoker.SendAsync(Get(), default);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).ShouldBe("payload-v1");
        stub.IfNoneMatchSeen[1].ShouldBe("\"v1\"");
        stub.CallCount.ShouldBe(2); // the handler still makes the (quota-free) conditional request
    }

    [Fact]
    public async Task Replayed_response_carries_the_fresh_headers_from_the_304()
    {
        var stub = ETagStub();
        using var invoker = new HttpMessageInvoker(new ConditionalGetHandler(stub));

        await invoker.SendAsync(Get(), default);
        var second = await invoker.SendAsync(Get(), default);

        second.Headers.GetValues("X-RateLimit-Remaining").ShouldHaveSingleItem().ShouldBe("4999");
    }

    [Fact]
    public async Task Responses_without_an_etag_are_not_cached()
    {
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("no-etag") });
        using var invoker = new HttpMessageInvoker(new ConditionalGetHandler(stub));

        await invoker.SendAsync(Get(), default);
        await invoker.SendAsync(Get(), default);

        stub.IfNoneMatchSeen.ShouldAllBe(tag => tag == null);
    }

    [Fact]
    public async Task Non_get_requests_are_not_made_conditional()
    {
        var stub = ETagStub();
        using var invoker = new HttpMessageInvoker(new ConditionalGetHandler(stub));

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, Uri), default);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, Uri), default);

        stub.IfNoneMatchSeen.ShouldAllBe(tag => tag == null);
    }
}
