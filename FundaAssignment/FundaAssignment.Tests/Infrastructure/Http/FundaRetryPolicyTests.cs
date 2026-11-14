using System.Net;
using System.Net.Http.Headers;
using FundaAssignment.Infrastructure.Http;
using FundaAssignment.Infrastructure.Options;
using FundaAssignment.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

namespace FundaAssignment.Tests.Infrastructure.Http;

[TestFixture]
public class FundaRetryPolicyTests
{
    private static readonly FundaApiOptions Options = new()
    {
        MaxRetryAttempts = 4,
        RetryBaseDelaySeconds = 5,
        BlockedRetryDelaySeconds = 30
    };

    // --- Block page detection ---

    [Test]
    public void Html_with_status_200_is_a_block_page()
    {
        var response = FakeHttpMessageHandler.BlockPage();

        var isBlockPage = FundaRetryPolicy.IsBlockPage(response);

        Assert.That(isBlockPage, Is.True);
    }

    [TestCase("application/json")]
    [TestCase("text/json")]
    public void Json_is_not_a_block_page(string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", null, mediaType)
        };

        var isBlockPage = FundaRetryPolicy.IsBlockPage(response);

        Assert.That(isBlockPage, Is.False);
    }

    [Test]
    public void An_error_status_is_not_a_block_page()
    {
        var response = FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);

        var isBlockPage = FundaRetryPolicy.IsBlockPage(response);

        Assert.That(isBlockPage, Is.False);
    }

    // --- Which outcomes are retried ---

    [TestCase(HttpStatusCode.RequestTimeout)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task Retries_transient_status_codes(HttpStatusCode statusCode)
    {
        var outcome = Outcome.FromResult(FakeHttpMessageHandler.Status(statusCode));

        var shouldRetry = await ShouldRetry(outcome);

        Assert.That(shouldRetry, Is.True);
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task Does_not_retry_client_errors(HttpStatusCode statusCode)
    {
        var outcome = Outcome.FromResult(FakeHttpMessageHandler.Status(statusCode));

        var shouldRetry = await ShouldRetry(outcome);

        Assert.That(shouldRetry, Is.False);
    }

    [Test]
    public async Task Retries_network_failures()
    {
        var outcome = Outcome.FromException<HttpResponseMessage>(new HttpRequestException("Connection reset"));

        var shouldRetry = await ShouldRetry(outcome);

        Assert.That(shouldRetry, Is.True);
    }

    [Test]
    public async Task Does_not_retry_unrelated_exceptions()
    {
        var outcome = Outcome.FromException<HttpResponseMessage>(new InvalidOperationException());

        var shouldRetry = await ShouldRetry(outcome);

        Assert.That(shouldRetry, Is.False);
    }

    // --- How long to wait ---

    [TestCase(1, 60)]
    [TestCase(2, 90)]
    [TestCase(3, 120)]
    public async Task Waits_longer_after_each_block_page(int attemptNumber, int expectedSeconds)
    {
        var outcome = Outcome.FromResult(FakeHttpMessageHandler.BlockPage());

        var delay = await GetDelay(outcome, attemptNumber);

        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [Test]
    public async Task Honors_a_retry_after_delay_from_the_server()
    {
        var response = FakeHttpMessageHandler.Status(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        var delay = await GetDelay(Outcome.FromResult(response), attemptNumber: 0);

        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(12)));
    }

    [Test]
    public async Task Honors_a_retry_after_date_from_the_server()
    {
        var response = FakeHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(20));

        var delay = await GetDelay(Outcome.FromResult(response), attemptNumber: 0);

        Assert.That(delay, Is.GreaterThan(TimeSpan.FromSeconds(15)).And.LessThanOrEqualTo(TimeSpan.FromSeconds(20)));
    }

    [Test]
    public async Task Uses_the_regular_exponential_backoff_for_other_failures()
    {
        var outcome = Outcome.FromResult(FakeHttpMessageHandler.Status(HttpStatusCode.BadGateway));

        var delay = await GetDelay(outcome, attemptNumber: 0);

        // null tells Polly to use the configured exponential backoff.
        Assert.That(delay, Is.Null);
    }

    // --- The whole policy in a pipeline ---

    [Test]
    public async Task Recovers_when_a_block_page_is_followed_by_json()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            FakeHttpMessageHandler.BlockPage(),
            FakeHttpMessageHandler.Status(HttpStatusCode.GatewayTimeout),
            FakeHttpMessageHandler.Json("{}")
        ]);
        var attempts = 0;

        var response = await CreatePipeline().ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromResult(responses.Dequeue());
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(FundaRetryPolicy.IsBlockPage(response), Is.False);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Gives_up_after_the_maximum_number_of_retries()
    {
        var attempts = 0;

        var response = await CreatePipeline().ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromResult(FakeHttpMessageHandler.BlockPage());
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(1 + Options.MaxRetryAttempts));
            Assert.That(FundaRetryPolicy.IsBlockPage(response), Is.True);
        }
    }

    private static ResiliencePipeline<HttpResponseMessage> CreatePipeline()
    {
        var strategy = FundaRetryPolicy.Create(Options, NullLogger.Instance);

        // Keep the real retry decisions but skip the waiting, so the test runs instantly.
        strategy.DelayGenerator = _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);

        return new ResiliencePipelineBuilder<HttpResponseMessage>().AddRetry(strategy).Build();
    }

    private static async Task<bool> ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        var strategy = FundaRetryPolicy.Create(Options, NullLogger.Instance);
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            return await strategy.ShouldHandle(
                new RetryPredicateArguments<HttpResponseMessage>(context, outcome, attemptNumber: 0));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static async Task<TimeSpan?> GetDelay(Outcome<HttpResponseMessage> outcome, int attemptNumber)
    {
        var strategy = FundaRetryPolicy.Create(Options, NullLogger.Instance);
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            return await strategy.DelayGenerator!(
                new RetryDelayGeneratorArguments<HttpResponseMessage>(context, outcome, attemptNumber));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
