using System.Text.Json.Serialization;

namespace FundaAssignment.Infrastructure.API.Dtos;

internal sealed record PagingDto
{
    [JsonPropertyName("AantalPaginas")]
    public int TotalPages { get; init; }

    [JsonPropertyName("HuidigePagina")]
    public int CurrentPage { get; init; }
}