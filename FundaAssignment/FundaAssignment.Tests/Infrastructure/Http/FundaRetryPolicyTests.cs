using System.Net;
using System.Net.Http.Headers;
using FundaAssignment.Infrastructure.Http;
using FundaAssignment.Infrastructure.Options;
using FundaAssignment.Tests.Fakes;
using Microsoft.Extensions.Http.Resilience;
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

    [TestCase(HttpStatusCode.OK, "text/html", true)]
    [TestCase(HttpStatusCode.OK, "application/json", false)]
    [TestCase(HttpStatusCode.OK, "text/json", false)]
    [TestCase(HttpStatusCode.NotFound, "text/plain", false)]
    public void Detects_an_html_response_instead_of_json(HttpStatusCode status, string mediaType, bool expected)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("body", null, mediaType)
        };

        var isBlockPage = FundaRetryPolicy.IsBlockPage(response);

        Assert.That(isBlockPage, Is.EqualTo(expected));
    }

    // --- Which outcomes are retried ---

    // 408, 429 and 5xx can succeed on a later try; other 4xx errors fail the same way every time.
    [TestCase(HttpStatusCode.RequestTimeout, true)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    [TestCase(HttpStatusCode.InternalServerError, true)]
    [TestCase(HttpStatusCode.ServiceUnavailable, true)]
    [TestCase(HttpStatusCode.BadRequest, false)]
    [TestCase(HttpStatusCode.NotFound, false)]
    public async Task Retries_only_status_codes_that_can_succeed_later(HttpStatusCode status, bool expected)
    {
        var outcome = Outcome.FromResult(FakeHttpMessageHandler.Status(status));

        var shouldRetry = await ShouldRetry(outcome);

        Assert.That(shouldRetry, Is.EqualTo(expected));
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

    private static Task<bool> ShouldRetry(Outcome<HttpResponseMessage> outcome) =>
        CallPolicy((strategy, context) => strategy.ShouldHandle(
            new RetryPredicateArguments<HttpResponseMessage>(context, outcome, attemptNumber: 0)));

    private static Task<TimeSpan?> GetDelay(Outcome<HttpResponseMessage> outcome, int attemptNumber) =>
        CallPolicy((strategy, context) => strategy.DelayGenerator!(
            new RetryDelayGeneratorArguments<HttpResponseMessage>(context, outcome, attemptNumber)));

    // Polly passes a context to every decision; borrow one from its pool and always return it.
    private static async Task<T> CallPolicy<T>(
        Func<HttpRetryStrategyOptions, ResilienceContext, ValueTask<T>> call)
    {
        var strategy = FundaRetryPolicy.Create(Options, NullLogger.Instance);
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            return await call(strategy, context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
