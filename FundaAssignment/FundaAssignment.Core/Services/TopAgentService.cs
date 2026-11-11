using FundaAssignment.Core.Interfaces;
using FundaAssignment.Core.Models;

namespace FundaAssignment.Core.Services;

public sealed class TopAgentsService
{
    private readonly IListingSource _listingSource;

    public TopAgentsService(IListingSource listingSource)
    {
        _listingSource = listingSource ?? throw new ArgumentNullException(nameof(listingSource));
    }
    
    public async Task<IReadOnlyList<AgentRanking>> GetTopAgentsAsync(
        string searchQuery,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchQuery);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var listings = await _listingSource
            .GetListingsAsync(searchQuery, cancellationToken)
            .ToListAsync(cancellationToken);

        // Listings can shift between pages while paginating, so the same
        // listing may appear twice. Count each listing only once.
        var uniqueListings = listings.DistinctBy(listing => listing.Id);

        var rankings = uniqueListings
            .GroupBy(listing => listing.AgentId)
            .Select(group => new AgentRanking
            {
                AgentId = group.Key,
                AgentName = group.First().AgentName,
                ListingCount = group.Count()
            });

        return rankings
            .OrderByDescending(ranking => ranking.ListingCount)
            .ThenBy(ranking => ranking.AgentName, StringComparer.OrdinalIgnoreCase)   
            .Take(count)
            .ToList();
    }
}