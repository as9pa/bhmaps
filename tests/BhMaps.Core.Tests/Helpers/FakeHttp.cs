using System.Net;
using System.Net.Http;

namespace BhMaps.Core.Tests.Helpers;

/// <summary>An HttpMessageHandler that answers from a table of urls, so UpdateClient can be tested without a
/// network. A url with no entry answers 404; a url mapped to null throws, standing in for no connection.</summary>
public sealed class FakeHttp : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requested { get; } = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttp Text(string url, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[url] = () => new HttpResponseMessage(status) { Content = new StringContent(body) };
        return this;
    }

    public FakeHttp Bytes(string url, byte[] body)
    {
        _routes[url] = () =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentLength = body.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        return this;
    }

    public FakeHttp Throws(string url)
    {
        _routes[url] = () => throw new HttpRequestException("no connection");
        return this;
    }

    /// <summary>Never completes until the token is cancelled, so the 10 s timeout can be tested with a token the
    /// test cancels itself rather than by waiting ten seconds.</summary>
    public FakeHttp Hangs(string url)
    {
        _routes[url] = null!;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requested.Add(url);
        Requests.Add(request);
        if (!_routes.TryGetValue(url, out var route))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }

        if (route is null)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        return route!();
    }
}
