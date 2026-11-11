namespace FundaAssignment.Core.Models;

public sealed record Listing
{
    public required long Id { get; init; }
    public required int AgentId { get; init; }
    public required string AgentName { get; init; }
}