using System.Runtime.CompilerServices;
using System.Text.Json;
using FundaAssignment.Core.Interfaces;
using FundaAssignment.Core.Models;
using FundaAssignment.Infrastructure.API.Dtos;
using FundaAssignment.Infrastructure.Http;
using FundaAssignment.Infrastructure.Api.Mapping;
using FundaAssignment.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundaAssignment.Infrastructure.Api;

public class FundaApiClient : IListingSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly FundaApiOptions _options;
    private readonly ILogger<FundaApiClient> _logger;
    
    public FundaApiClient(
        HttpClient httpClient,
        IOptions<FundaApiOptions> options,
        ILogger<FundaApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }
    
    public async IAsyncEnumerable<Listing> GetListingsAsync(
        ListingSearch search,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var searchQuery = ToSearchQuery(search);
        var page = 1;
        var totalPages = 1; // corrected after the first response

        while (page <= totalPages)
        {
            var response = await FetchPageAsync(searchQuery, page, cancellationToken);
            totalPages = response.Paging.TotalPages;

            if (page < totalPages && response.Objects.Count < _options.PageSize)
            {
                _logger.LogWarning(
                    "Page {Page} returned {Count} objects but {PageSize} were requested; " +
                    "the API may be capping the page size, so the page count could be unreliable.",
                    page, response.Objects.Count, _options.PageSize);
            }
            
            _logger.LogInformation(
                "Fetched page {Page}/{TotalPages} for '{Query}' ({Total} objects in total)",
                page, totalPages, searchQuery, response.TotalObjectCount);

            foreach (var item in response.Objects)
            {
                foreach (var dto in Flatten(item))
                {
                    yield return dto.ToDomain();
                }
            }

            page++;
        }
    }
    
    private async Task<FundaResponseDto> FetchPageAsync(
        string searchQuery, int page, CancellationToken cancellationToken)
    {
        var zo = Uri.EscapeDataString(searchQuery);
        var relativeUrl = $"?type={_options.OfferType}&zo={zo}&page={page}&pagesize={_options.PageSize}";

        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (FundaRetryPolicy.IsBlockPage(response))
        {
            throw new FundaApiBlockedException(page, Truncate(body, 80));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Funda API page {page} failed after retries: HTTP {(int)response.StatusCode}, " +
                $"content type '{response.Content.Headers.ContentType}'. Body starts with: {Truncate(body)}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return JsonSerializer.Deserialize<FundaResponseDto>(body, JsonOptions)
               ?? throw new InvalidOperationException($"The Funda API returned an empty response for page {page}.");
    }
    
    private static string Truncate(string value, int max = 200) =>
        value.Length <= max ? value : value[..max] + "...";
    
    private static IEnumerable<FundaObjectDto> Flatten(FundaObjectDto parent)
    {
        yield return parent;
        foreach (var child in parent.Children ?? [])
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
    
    private static string ToSearchQuery(ListingSearch search)
    {
        var segments = new List<string>
        {
            Slugify(search.City)
        };

        if (search.RequiresGarden)
        {
            segments.Add("tuin");
        }

        return $"/{string.Join('/', segments)}/";
    }

    private static string Slugify(string value) =>
        value.Trim().ToLowerInvariant().Replace(' ', '-');
}