using System.Net;
using System.Text;

namespace FundaAssignment.Tests.Fakes;

/// <summary>
/// Answers requests from a callback and records every request URI, so tests never touch the real API.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public List<Uri> RequestedUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Like a real handler, honor cancellation, so tests can check the token is passed through.
        cancellationToken.ThrowIfCancellationRequested();
        RequestedUris.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage BlockPage() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "<!doctype html><title>Je bent bijna op de pagina die je zoekt [funda]</title>",
            Encoding.UTF8,
            "text/html")
    };

    public static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode)
    {
        Content = new StringContent("error", Encoding.UTF8, "text/plain")
    };
}
