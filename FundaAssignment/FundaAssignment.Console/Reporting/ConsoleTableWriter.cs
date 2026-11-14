using FundaAssignment.Core.Interfaces;
using FundaAssignment.Core.Models;

namespace FundaAssignment.Console.Reporting;

public sealed class ConsoleTableWriter(TextWriter output) : IRankingWriter
{
    public void Write(string title, IReadOnlyList<AgentRanking> rankings)
    {
        output.WriteLine();
        output.WriteLine(title);
        output.WriteLine(new string('=', title.Length));

        if (rankings.Count == 0)
        {
            output.WriteLine("No listings found.");
            return;
        }

        var rankWidth = Math.Max("#".Length, rankings.Count.ToString().Length);
        var nameWidth = Math.Max("Agent".Length, rankings.Max(r => r.AgentName.Length));
        var idWidth = Math.Max("Id".Length, rankings.Max(r => r.AgentId.ToString().Length));
        var countWidth = Math.Max("Listings".Length, rankings.Max(r => r.ListingCount.ToString().Length));

        var header = Row("#", "Agent", "Id", "Listings");
        output.WriteLine(header);
        output.WriteLine(new string('-', header.Length));

        for (var i = 0; i < rankings.Count; i++)
        {
            var r = rankings[i];
            output.WriteLine(Row(
                (i + 1).ToString(), r.AgentName, r.AgentId.ToString(), r.ListingCount.ToString()));
        }

        return;

        string Row(string rank, string name, string id, string count) =>
            $"{rank.PadLeft(rankWidth)}  {name.PadRight(nameWidth)}  {id.PadLeft(idWidth)}  {count.PadLeft(countWidth)}";
    }
}