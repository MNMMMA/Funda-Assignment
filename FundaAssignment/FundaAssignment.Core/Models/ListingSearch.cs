namespace FundaAssignment.Core.Models;

public sealed record ListingSearch
{
    public required string City { get; init; }
    public bool RequiresGarden { get; init; }
}