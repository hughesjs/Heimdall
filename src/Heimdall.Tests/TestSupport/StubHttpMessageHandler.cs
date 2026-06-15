namespace Heimdall.Tests.TestSupport;

/// <summary>
/// A scriptable inner <see cref="HttpMessageHandler"/> for exercising delegating handlers. Records the
/// number of network calls and the <c>If-None-Match</c> tag seen on each request.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public List<string?> IfNoneMatchSeen { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        IfNoneMatchSeen.Add(request.Headers.IfNoneMatch.FirstOrDefault()?.Tag);
        var response = responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
