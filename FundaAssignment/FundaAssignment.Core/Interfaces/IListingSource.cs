using FundaAssignment.Core.Models;

namespace FundaAssignment.Core.Interfaces;

public interface IListingSource
{
    IAsyncEnumerable<Listing> GetListingsAsync(
        string searchQuery,
        CancellationToken cancellationToken = default);
}