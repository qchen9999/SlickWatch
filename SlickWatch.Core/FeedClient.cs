using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SlickWatch.Core;

public sealed record FeedResponse(string? Content, bool NotModified = false);
public sealed class FeedRequestException(string message, DateTimeOffset? retryAt = null) : Exception(message)
{
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
public interface IFeedClient
{
    Task<FeedResponse> GetAsync(string url, bool conditional, CancellationToken cancellationToken);
}

public sealed class FeedClient : IFeedClient, IDisposable
{
    public const string FrontpageUrl = "https://slickdeals.net/newsearch.php?mode=frontpage&searcharea=deals&searchin=first&rss=1";
    public const string PopularUrl = "https://slickdeals.net/newsearch.php?mode=popdeals&searcharea=deals&searchin=first&rss=1";
    private readonly HttpClient _http;
    private readonly Dictionary<string, (EntityTagHeaderValue? Tag, DateTimeOffset? Modified)> _cache = [];
    public FeedClient()
    {
        _http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false });
        _http.Timeout = TimeSpan.FromSeconds(25);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SlickWatch/1.0 (personal RSS desktop reader)");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }
    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "slickdeals.net" || uri.Host == "www.slickdeals.net");

    public async Task<FeedResponse> GetAsync(string url, bool conditional, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (!Allowed(uri)) throw new FeedRequestException("The site returned an unsupported redirect.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (conditional && _cache.TryGetValue(url, out var cached))
            {
                if (cached.Tag is not null) request.Headers.IfNoneMatch.Add(cached.Tag);
                request.Headers.IfModifiedSince = cached.Modified;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified) return new(null, true);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                uri = response.Headers.Location is { } location ? new Uri(uri, location)
                    : throw new FeedRequestException("The site returned an empty redirect.");
                continue;
            }
            if ((int)response.StatusCode is 429 or 503 or 403)
            {
                var retry = response.Headers.RetryAfter;
                var retryAt = retry?.Date ?? DateTimeOffset.UtcNow + (retry?.Delta ?? TimeSpan.FromMinutes(15));
                throw new FeedRequestException($"Slickdeals returned {(int)response.StatusCode}. Checks will resume after {retryAt.ToLocalTime():t}.", retryAt);
            }
            if (!response.IsSuccessStatusCode) throw new FeedRequestException($"Slickdeals returned HTTP {(int)response.StatusCode}.");
            const int limit = 6_000_000;
            if (response.Content.Headers.ContentLength > limit) throw new FeedRequestException("The response was larger than expected.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            byte[] buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + read > limit) throw new FeedRequestException("The response was larger than expected.");
                output.Write(buffer, 0, read);
            }
            if (conditional) _cache[url] = (response.Headers.ETag, response.Content.Headers.LastModified);
            return new(Encoding.UTF8.GetString(output.ToArray()));
        }
        throw new FeedRequestException("The site returned too many redirects.");
    }
    public void Dispose() => _http.Dispose();
}
