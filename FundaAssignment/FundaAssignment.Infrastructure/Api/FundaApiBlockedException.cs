namespace FundaAssignment.Infrastructure.Api;

/// <summary>
/// Thrown when Funda keeps answering with its HTML block page instead of JSON,
/// even after all retries. This means the client is temporarily blocked,
/// not that the request itself is wrong.
/// </summary>
public sealed class FundaApiBlockedException(int page, string? bodyStart)
    : HttpRequestException(
        $"Funda is temporarily blocking requests from this machine: page {page} returned " +
        "its HTML block page instead of JSON, even after retrying. " +
        "Wait 10-15 minutes without calling the API and run the program again. " +
        $"Body starts with: {bodyStart}")
{
    public int Page { get; } = page;
}
