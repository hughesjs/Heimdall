using System.Collections.Concurrent;
using System.Net;

namespace Heimdall.Core.GitHub;

/// <summary>
/// Adds GitHub conditional-request support beneath Octokit. Caches each GET's ETag and response body
/// by URI, sends <c>If-None-Match</c> on the next request for that URI, and on a <c>304 Not Modified</c>
/// (which does not count against the rate limit) replays the cached body as a synthesised <c>200</c> so
/// Octokit deserialises it normally. Fresh headers from the 304 (e.g. rate-limit counters) are kept.
/// </summary>
public sealed class ConditionalGetHandler : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public ConditionalGetHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    /// <summary>Parameterless ctor for tests that assign <see cref="DelegatingHandler.InnerHandler"/> separately.</summary>
    public ConditionalGetHandler() { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is null)
            return await base.SendAsync(request, cancellationToken);

        var key = request.RequestUri.AbsoluteUri;
        var cached = _cache.TryGetValue(key, out var entry) ? entry : null;
        if (cached is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            return ReplayFromCache(request, response, cached);

        if (response.IsSuccessStatusCode && response.Headers.ETag is not null)
            await StoreAsync(key, response, cancellationToken);

        return response;
    }

    private async Task StoreAsync(string key, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentHeaders = response.Content.Headers.ToList();
        _cache[key] = new CacheEntry(response.Headers.ETag!.ToString(), body, contentHeaders);

        // Re-attach buffered content so downstream readers always get a fresh, readable stream.
        response.Content = BuildContent(body, contentHeaders);
    }

    private static HttpResponseMessage ReplayFromCache(HttpRequestMessage request, HttpResponseMessage notModified, CacheEntry cached)
    {
        var synthesised = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Version = notModified.Version,
            Content = BuildContent(cached.Body, cached.ContentHeaders)
        };

        // Carry the 304's fresh response headers (ETag, rate-limit counters, Link, …) onto the replay.
        foreach (var (name, values) in notModified.Headers)
            synthesised.Headers.TryAddWithoutValidation(name, values);

        notModified.Dispose();
        return synthesised;
    }

    private static ByteArrayContent BuildContent(byte[] body, IEnumerable<KeyValuePair<string, IEnumerable<string>>> contentHeaders)
    {
        var content = new ByteArrayContent(body);
        foreach (var (name, values) in contentHeaders)
        {
            // Content-Length is recomputed by ByteArrayContent; copying it would conflict.
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            content.Headers.TryAddWithoutValidation(name, values);
        }

        return content;
    }

    private sealed record CacheEntry(string ETag, byte[] Body, List<KeyValuePair<string, IEnumerable<string>>> ContentHeaders);
}
