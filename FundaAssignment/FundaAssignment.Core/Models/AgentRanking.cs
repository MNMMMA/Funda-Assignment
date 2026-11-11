namespace FundaAssignment.Core.Models;

public sealed record AgentRanking
{
    public required int AgentId { get; init; }
    public required string AgentName { get; init; }
    public required int ListingCount { get; init; }
}