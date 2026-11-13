using FundaAssignment.Core.Models;

namespace FundaAssignment.Core.Interfaces;

public interface IListingSource
{
    IAsyncEnumerable<Listing> GetListingsAsync(
        ListingSearch search,
        CancellationToken cancellationToken = default);
}