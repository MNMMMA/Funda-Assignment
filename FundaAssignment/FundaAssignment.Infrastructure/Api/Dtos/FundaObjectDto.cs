using System.Text.Json.Serialization;

namespace FundaAssignment.Infrastructure.API.Dtos;

internal sealed record FundaObjectDto
{
    [JsonPropertyName("GlobalId")]
    public long GlobalId { get; init; }

    [JsonPropertyName("MakelaarId")]
    public int AgentId { get; init; }

    [JsonPropertyName("MakelaarNaam")]
    public string? AgentName { get; init; }
    
    [JsonPropertyName("ChildrenObjects")]
    public IReadOnlyList<FundaObjectDto>? Children { get; init; }
}