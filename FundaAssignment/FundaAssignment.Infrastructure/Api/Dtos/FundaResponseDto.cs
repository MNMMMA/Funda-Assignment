using System.Text.Json.Serialization;

namespace FundaAssignment.Infrastructure.Api.Dtos;

internal sealed record FundaResponseDto
{
    [JsonPropertyName("Objects")]
    public IReadOnlyList<FundaObjectDto> Objects { get; init; } = [];

    [JsonPropertyName("Paging")]
    public PagingDto Paging { get; init; } = new();

    [JsonPropertyName("TotaalAantalObjecten")]
    public int TotalObjectCount { get; init; }
}
