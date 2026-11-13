using FundaAssignment.Core.Models;
using FundaAssignment.Infrastructure.Api.Dtos;

namespace FundaAssignment.Infrastructure.Api.Mapping;

internal static class ListingMapper
{
    public static Listing ToDomain(this FundaObjectDto dto) =>
        new()
        {
            Id = dto.GlobalId,
            AgentId = dto.AgentId,
            AgentName = string.IsNullOrWhiteSpace(dto.AgentName)
                ? $"Unknown ({dto.AgentId})"
                : dto.AgentName.Trim()
        };
}