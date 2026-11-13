using FundaAssignment.Core.Models;

namespace FundaAssignment.Console.Models;

internal sealed record Report
{
    public required string Title { get; init; }
    public required ListingSearch Search { get; init; }
}