using FundaAssignment.Core.Models;

namespace FundaAssignment.Console.Reporting;

internal sealed record Report
{
    public required string Title { get; init; }
    public required ListingSearch Search { get; init; }
}