using FundaAssignment.Core.Models;

namespace FundaAssignment.Core.Interfaces;

public interface IRankingWriter
{
    void Write(string title, IReadOnlyList<AgentRanking> rankings);
}