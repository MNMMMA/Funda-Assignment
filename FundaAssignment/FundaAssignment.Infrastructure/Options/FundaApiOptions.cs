namespace FundaAssignment.Infrastructure.Options;

public class FundaApiOptions
{
    public const int MaxPageSize = 25;

    public const int MaxRequestsPerMinute = 90;
    public const string SectionName = "Funda";

    public string BaseUrl { get; init; } = "https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/";
    public string ApiKey { get; init; } = "";
    public int PageSize { get; init; } = MaxPageSize;
    public string OfferType { get; init; } = "koop";   // "koop" = for sale
    
    public int RequestsPerMinute { get; init; } = 60;
    public int MaxRetryAttempts { get; init; } = 4;
    public int RetryBaseDelaySeconds { get; init; } = 5;

    // Base wait after Funda returns an HTML page instead of JSON; grows linearly per attempt (30s, 60s, 90s, ...).
    public int BlockedRetryDelaySeconds { get; init; } = 30;
}