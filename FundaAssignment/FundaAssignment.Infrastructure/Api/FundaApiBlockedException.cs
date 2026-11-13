namespace FundaAssignment.Infrastructure.Api;

/// <summary>
/// Thrown when Funda keeps answering with an HTML page instead of JSON, even after all retries.
/// This happens when requests are rejected (for example for exceeding the rate limit)
/// or when the API key is no longer accepted; the request itself is not malformed.
/// </summary>
public sealed class FundaApiBlockedException(int page, string? bodyStart)
    : HttpRequestException(
        $"Funda returned an HTML page instead of JSON for page {page}, even after retrying. " +
        "This usually means requests are being rejected for exceeding the rate limit, " +
        "or the API key is no longer valid. Wait a while and try again; " +
        "if it keeps happening, check the API key with Funda. " +
        $"Body starts with: {bodyStart}")
{
    public int Page { get; } = page;
}
