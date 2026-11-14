using FundaAssignment.Infrastructure;
using FundaAssignment.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FundaAssignment.Tests.Infrastructure;

[TestFixture]
public class FundaApiOptionsValidationTests
{
    [TestCase(1)]
    [TestCase(60)]
    [TestCase(FundaApiOptions.MaxRequestsPerMinute)]
    public void Accepts_a_request_rate_within_the_limit(int requestsPerMinute)
    {
        using var services = BuildServices(("Funda:RequestsPerMinute", requestsPerMinute.ToString()));

        var options = services.GetRequiredService<IOptions<FundaApiOptions>>().Value;

        Assert.That(options.RequestsPerMinute, Is.EqualTo(requestsPerMinute));
    }

    [TestCase("Funda:RequestsPerMinute", "0", "RequestsPerMinute must be between 1 and 90")]
    [TestCase("Funda:RequestsPerMinute", "91", "RequestsPerMinute must be between 1 and 90")]
    [TestCase("Funda:RequestsPerMinute", "500", "rejects more than 100 requests per minute")]
    [TestCase("Funda:ApiKey", "", "ApiKey is not configured")]
    [TestCase("Funda:BaseUrl", "not a url", "BaseUrl must be an absolute http or https URL")]
    [TestCase("Funda:BaseUrl", "/feeds/Aanbod.svc/json/", "BaseUrl must be an absolute http or https URL")]
    [TestCase("Funda:BaseUrl", "ftp://funda.test/feeds/", "BaseUrl must be an absolute http or https URL")]
    public void Rejects_an_invalid_setting_with_a_clear_message(string key, string value, string expectedMessage)
    {
        using var services = BuildServices((key, value));

        var ex = Assert.Throws<OptionsValidationException>(
            () => _ = services.GetRequiredService<IOptions<FundaApiOptions>>().Value);

        Assert.That(ex!.Failures, Has.One.Contains(expectedMessage));
    }

    private static ServiceProvider BuildServices(params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?> { ["Funda:ApiKey"] = "test-key" };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddFundaApi(configuration)
            .BuildServiceProvider();
    }
}
