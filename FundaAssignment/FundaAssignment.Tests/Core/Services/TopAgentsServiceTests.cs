using System.Runtime.CompilerServices;
using FundaAssignment.Core.Interfaces;
using FundaAssignment.Core.Models;
using FundaAssignment.Core.Services;

namespace FundaAssignment.Tests.Core.Services;

[TestFixture]
public class TopAgentsServiceTests
{
    private static readonly ListingSearch Amsterdam = new() { City = "Amsterdam" };
    private static readonly string[] ExpectedResult = ["Alpha", "bravo", "charlie"];

    [Test]
    public async Task Ranks_agents_by_number_of_listings_descending()
    {
        var service = CreateService(
            Listing(1, agentId: 10, "Alpha"),
            Listing(2, agentId: 20, "Bravo"),
            Listing(3, agentId: 20, "Bravo"),
            Listing(4, agentId: 30, "Charlie"),
            Listing(5, agentId: 30, "Charlie"),
            Listing(6, agentId: 30, "Charlie"));

        var result = await service.GetTopAgentsAsync(Amsterdam);

        Assert.That(result.Select(r => (r.AgentId, r.AgentName, r.ListingCount)), Is.EqualTo(new[]
        {
            (30, "Charlie", 3),
            (20, "Bravo", 2),
            (10, "Alpha", 1)
        }));
    }

    [TestCase(15, 10, 10, TestName = "More agents than requested: capped at count")]
    [TestCase(2, 10, 2, TestName = "Fewer agents than requested: all of them")]
    [TestCase(0, 10, 0, TestName = "No listings: empty result")]
    public async Task Returns_at_most_count_agents(int agents, int count, int expected)
    {
        var listings = Enumerable.Range(1, agents)
            .Select(i => Listing(i, agentId: i, $"Agent {i:00}"))
            .ToArray();
        var service = CreateService(listings);

        var result = await service.GetTopAgentsAsync(Amsterdam, count);

        Assert.That(result, Has.Count.EqualTo(expected));
    }

    [Test]
    public async Task Counts_a_listing_only_once_when_it_appears_on_two_pages()
    {
        // Listings can shift between pages while paginating, so the API may return one twice.
        var service = CreateService(
            Listing(1, agentId: 10, "Alpha"),
            Listing(1, agentId: 10, "Alpha"),
            Listing(2, agentId: 20, "Bravo"));

        var result = await service.GetTopAgentsAsync(Amsterdam);

        Assert.That(result.Single(r => r.AgentId == 10).ListingCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Orders_agents_with_equal_counts_by_name_ignoring_case()
    {
        var service = CreateService(
            Listing(1, agentId: 10, "charlie"),
            Listing(2, agentId: 20, "Alpha"),
            Listing(3, agentId: 30, "bravo"));

        var result = await service.GetTopAgentsAsync(Amsterdam);

        Assert.That(result.Select(r => r.AgentName), Is.EqualTo(ExpectedResult));
    }

    [Test]
    public async Task Groups_by_agent_id_not_by_name()
    {
        // Two different agents can share a display name.
        var service = CreateService(
            Listing(1, agentId: 10, "Same Name"),
            Listing(2, agentId: 20, "Same Name"));

        var result = await service.GetTopAgentsAsync(Amsterdam);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.All(r => r.ListingCount == 1), Is.True);
    }

    [Test]
    public async Task Passes_the_search_to_the_listing_source()
    {
        var source = new FakeListingSource([]);
        var service = new TopAgentsService(source);
        var search = new ListingSearch { City = "Amsterdam", RequiresGarden = true };

        await service.GetTopAgentsAsync(search);

        Assert.That(source.ReceivedSearch, Is.EqualTo(search));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Rejects_a_count_that_is_not_positive(int count)
    {
        var service = CreateService();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetTopAgentsAsync(Amsterdam, count));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Rejects_an_empty_city(string city)
    {
        var service = CreateService();

        Assert.ThrowsAsync<ArgumentException>(() => service.GetTopAgentsAsync(new ListingSearch { City = city }));
    }

    private static TopAgentsService CreateService(params Listing[] listings) =>
        new(new FakeListingSource(listings));

    private static Listing Listing(long id, int agentId, string agentName) =>
        new() { Id = id, AgentId = agentId, AgentName = agentName };

    private sealed class FakeListingSource(IReadOnlyList<Listing> listings) : IListingSource
    {
        public ListingSearch? ReceivedSearch { get; private set; }

        public async IAsyncEnumerable<Listing> GetListingsAsync(
            ListingSearch search,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedSearch = search;
            foreach (var listing in listings)
            {
                await Task.Yield();
                yield return listing;
            }
        }
    }
}
