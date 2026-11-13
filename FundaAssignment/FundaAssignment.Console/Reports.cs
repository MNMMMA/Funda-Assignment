using FundaAssignment.Console.Models;
using FundaAssignment.Core.Models;

namespace FundaAssignment.Console;

internal static class Reports
{
    public static IReadOnlyList<Report> All { get; } =
    [
        new Report
        {
            Title = "Top 10 makelaars in Amsterdam",
            Search = new ListingSearch { City = "Amsterdam" }
        },
        new Report
        {
            Title = "Top 10 makelaars in Amsterdam with a garden (tuin)",
            Search = new ListingSearch { City = "Amsterdam", RequiresGarden = true }
        }
    ];
}