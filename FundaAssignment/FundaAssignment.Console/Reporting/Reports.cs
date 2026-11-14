using FundaAssignment.Core.Models;

namespace FundaAssignment.Console.Reporting;

internal static class Reports
{
    public static IReadOnlyList<Report> All { get; } =
    [
        new()
        {
            Title = "Top 10 makelaars in Amsterdam",
            Search = new ListingSearch { City = "Amsterdam" }
        },
        new()
        {
            Title = "Top 10 makelaars in Amsterdam with a garden (tuin)",
            Search = new ListingSearch { City = "Amsterdam", RequiresGarden = true }
        }
    ];
}