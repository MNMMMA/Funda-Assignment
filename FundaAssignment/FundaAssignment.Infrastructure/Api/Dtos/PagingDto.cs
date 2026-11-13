using System.Text.Json.Serialization;

namespace FundaAssignment.Infrastructure.Api.Dtos;

internal sealed record PagingDto
{
    [JsonPropertyName("AantalPaginas")]
    public int TotalPages { get; init; }
}