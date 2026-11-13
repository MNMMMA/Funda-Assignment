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

    [TestCase(0)]
    [TestCase(FundaApiOptions.MaxRequestsPerMinute + 1)]
    [TestCase(500)]
    public void Rejects_a_request_rate_outside_the_limit_with_an_explanation(int requestsPerMinute)
    {
        using var services = BuildServices(("Funda:RequestsPerMinute", requestsPerMinute.ToString()));

        var ex = Assert.Throws<OptionsValidationException>(
            () => _ = services.GetRequiredService<IOptions<FundaApiOptions>>().Value);

        Assert.That(ex!.Failures.Single(), Does.Contain("RequestsPerMinute must be between 1 and 90")
            .And.Contain("rejects more than 100 requests per minute"));
    }

    [Test]
    public void Rejects_a_missing_api_key()
    {
        using var services = BuildServices(("Funda:ApiKey", ""));

        var ex = Assert.Throws<OptionsValidationException>(
            () => _ = services.GetRequiredService<IOptions<FundaApiOptions>>().Value);

        Assert.That(ex!.Failures, Has.One.Contains("ApiKey is not configured"));
    }

    [TestCase("not a url")]
    [TestCase("/feeds/Aanbod.svc/json/")]
    [TestCase("ftp://funda.test/feeds/")]
    public void Rejects_a_base_url_that_is_not_an_http_url(string baseUrl)
    {
        using var services = BuildServices(("Funda:BaseUrl", baseUrl));

        var ex = Assert.Throws<OptionsValidationException>(
            () => _ = services.GetRequiredService<IOptions<FundaApiOptions>>().Value);

        Assert.That(ex!.Failures, Has.One.Contains("BaseUrl must be an absolute http or https URL"));
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
