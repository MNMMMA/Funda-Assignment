using System.Net;
using System.Web;
using FundaAssignment.Core.Models;
using FundaAssignment.Infrastructure.Api;
using FundaAssignment.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundaAssignment.Tests.Infrastructure;

[TestFixture]
public class FundaApiClientTests
{
    private const string BaseAddress = "https://funda.test/feeds/Aanbod.svc/json/test-key/";
    private static readonly ListingSearch Amsterdam = new() { City = "Amsterdam" };

    [Test]
    public async Task Reads_every_page_reported_by_the_api()
    {
        var handler = new FakeHttpMessageHandler(request => PageOf(request) switch
        {
            1 => FakeHttpMessageHandler.Json(Page(1, totalPages: 3, Obj(1, 10, "Alpha"))),
            2 => FakeHttpMessageHandler.Json(Page(2, totalPages: 3, Obj(2, 20, "Bravo"))),
            3 => FakeHttpMessageHandler.Json(Page(3, totalPages: 3, Obj(3, 30, "Charlie"))),
            _ => throw new AssertionException("Requested a page beyond the last one.")
        });

        var listings = await CreateClient(handler).GetListingsAsync(Amsterdam).ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listings.Select(l => l.Id), Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(handler.RequestedUris.Select(PageOf), Is.EqualTo(new[] { 1, 2, 3 }));
        }
    }

    [Test]
    public async Task Returns_nothing_when_the_search_has_no_results()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(Page(1, totalPages: 0)));

        var listings = await CreateClient(handler).GetListingsAsync(Amsterdam).ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listings, Is.Empty);
            Assert.That(handler.RequestedUris, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Includes_child_objects_of_a_listing()
    {
        // A project listing can contain child listings, which can have children of their own.
        const string parent =
            """{ "GlobalId": 1, "MakelaarId": 10, "MakelaarNaam": "Alpha", "ChildrenObjects": [""" +
            """  { "GlobalId": 2, "MakelaarId": 10, "MakelaarNaam": "Alpha", "ChildrenObjects": [""" +
            """    { "GlobalId": 3, "MakelaarId": 10, "MakelaarNaam": "Alpha" } ] } ] }""";
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(Page(1, totalPages: 1, parent)));

        var listings = await CreateClient(handler).GetListingsAsync(Amsterdam).ToListAsync();

        Assert.That(listings.Select(l => l.Id), Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    [TestCase("Amsterdam", false, "/amsterdam/")]
    [TestCase("Amsterdam", true, "/amsterdam/tuin/")]
    [TestCase("  Den Haag ", false, "/den-haag/")]
    public async Task Builds_the_search_query_from_the_search(string city, bool requiresGarden, string expectedZo)
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(Page(1, totalPages: 1)));
        var search = new ListingSearch { City = city, RequiresGarden = requiresGarden };

        await CreateClient(handler).GetListingsAsync(search).ToListAsync();

        var uri = handler.RequestedUris.Single();
        var query = HttpUtility.ParseQueryString(uri.Query);
        using (Assert.EnterMultipleScope())
        {
            // Checked on the raw URL, so this also proves the slashes are sent unencoded.
            Assert.That(uri.AbsoluteUri, Does.Contain($"zo={expectedZo}&"));
            Assert.That(query["type"], Is.EqualTo("koop"));
            Assert.That(query["pagesize"], Is.EqualTo("25"));
        }
    }

    [Test]
    public async Task Escapes_special_characters_in_the_city_so_they_cannot_break_the_query()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(Page(1, totalPages: 1)));
        var search = new ListingSearch { City = "A&B?c=d" };

        await CreateClient(handler).GetListingsAsync(search).ToListAsync();

        var query = HttpUtility.ParseQueryString(handler.RequestedUris.Single().Query);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query["zo"], Is.EqualTo("/a&b?c=d/"));
            Assert.That(query["page"], Is.EqualTo("1"));
        }
    }

    [Test]
    public async Task Sends_requests_relative_to_the_base_address()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(Page(1, totalPages: 1)));

        await CreateClient(handler).GetListingsAsync(Amsterdam).ToListAsync();

        Assert.That(handler.RequestedUris.Single().GetLeftPart(UriPartial.Path), Is.EqualTo(BaseAddress));
    }

    [Test]
    public void Throws_FundaApiBlockedException_with_the_page_that_got_the_html_block_page()
    {
        var handler = new FakeHttpMessageHandler(request => PageOf(request) == 1
            ? FakeHttpMessageHandler.Json(Page(1, totalPages: 2, Obj(1, 10, "Alpha")))
            : FakeHttpMessageHandler.BlockPage());

        var ex = Assert.ThrowsAsync<FundaApiBlockedException>(
            async () => await CreateClient(handler).GetListingsAsync(Amsterdam).ToListAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Page, Is.EqualTo(2));
            Assert.That(ex.Message, Does.Contain("HTML page instead of JSON"));
        }
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void Throws_HttpRequestException_with_the_status_code_for_error_responses(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(statusCode));

        var ex = Assert.ThrowsAsync<HttpRequestException>(
            async () => await CreateClient(handler).GetListingsAsync(Amsterdam).ToListAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.Not.InstanceOf<FundaApiBlockedException>());
            Assert.That(ex!.StatusCode, Is.EqualTo(statusCode));
        }
    }

    [Test]
    public void Stops_when_cancelled()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(Page(1, totalPages: 1, Obj(1, 10, "Alpha"))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await CreateClient(handler).GetListingsAsync(Amsterdam, cts.Token).ToListAsync());
    }

    private static FundaApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) },
            Options.Create(new FundaApiOptions()),
            NullLogger<FundaApiClient>.Instance);

    private static int PageOf(HttpRequestMessage request) => PageOf(request.RequestUri!);

    private static int PageOf(Uri uri) => int.Parse(HttpUtility.ParseQueryString(uri.Query)["page"]!);

    private static string Obj(long id, int agentId, string agentName) =>
        $$"""{ "GlobalId": {{id}}, "MakelaarId": {{agentId}}, "MakelaarNaam": "{{agentName}}" }""";

    private static string Page(int page, int totalPages, params string[] objects) =>
        $$"""
          {
            "Objects": [{{string.Join(",", objects)}}],
            "Paging": { "AantalPaginas": {{totalPages}}, "HuidigePagina": {{page}} },
            "TotaalAantalObjecten": {{objects.Length}}
          }
          """;
}
