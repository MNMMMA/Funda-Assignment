namespace FundaAssignment.Infrastructure.Options;

public class FundaApiOptions
{
    public const int MaxPageSize = 25;
    public const string SectionName = "Funda";

    public string BaseUrl { get; set; } = "https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/";
    public string ApiKey { get; set; } = "";
    public int PageSize { get; set; } = MaxPageSize;
    public string OfferType { get; set; } = "koop";   // "koop" = for sale
    
    public int RequestsPerMinute { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 4;
    public int RetryBaseDelaySeconds { get; set; } = 5;

    // Base wait after Funda's HTML block page; grows linearly per attempt (30s, 60s, 90s, ...).
    public int BlockedRetryDelaySeconds { get; set; } = 30;
}