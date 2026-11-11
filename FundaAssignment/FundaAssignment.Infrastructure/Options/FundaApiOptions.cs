namespace FundaAssignment.Infrastructure.Options;

public class FundaApiOptions
{
    public const int MaxPageSize = 25;
    public const string SectionName = "Funda";

    public string BaseUrl { get; set; } = "http://partnerapi.funda.nl/feeds/Aanbod.svc/json/";
    public string ApiKey { get; set; } = "";
    public int PageSize { get; set; } = MaxPageSize;
    public string OfferType { get; set; } = "koop";   // "koop" = for sale
}