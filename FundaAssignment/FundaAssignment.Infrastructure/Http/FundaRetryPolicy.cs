using FundaAssignment.Infrastructure.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace FundaAssignment.Infrastructure.Http;

/// <summary>
/// Decides which failures are worth retrying and how long to wait between attempts.
/// </summary>
internal static class FundaRetryPolicy
{
    public static HttpRetryStrategyOptions Create(FundaApiOptions options, ILogger logger) => new()
    {
        OnRetry = args =>
        {
            var reason = args.Outcome.Result is { } r
                ? IsBlockPage(r) ? "Funda returned an HTML page instead of JSON" : $"HTTP {(int)r.StatusCode}"
                : args.Outcome.Exception?.Message ?? "unknown error";

            logger.LogWarning(
                "Request failed ({Reason}); retry {Attempt}/{Max} in {Delay:0}s.",
                reason, args.AttemptNumber + 1, options.MaxRetryAttempts, args.RetryDelay.TotalSeconds);
            return ValueTask.CompletedTask;
        },
        MaxRetryAttempts = options.MaxRetryAttempts,
        Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
        BackoffType = DelayBackoffType.Exponential,   // 5s, 10s, 20s, 40s by default
        UseJitter = true,                             // avoids retrying in lockstep

        // Retry-After is honored in GetDelay, because a custom delay generator replaces the built-in one.
        ShouldRetryAfterHeader = false,
        DelayGenerator = args => ValueTask.FromResult(GetDelay(args.Outcome, args.AttemptNumber, options)),
        ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome))
    };

    /// <summary>
    /// Funda sometimes answers 200 with an HTML page instead of JSON, for example when
    /// requests are rejected for exceeding the rate limit or the API key is no longer accepted.
    /// </summary>
    public static bool IsBlockPage(HttpResponseMessage response) =>
        response.IsSuccessStatusCode && !IsJson(response);

    private static TimeSpan? GetDelay(
        Outcome<HttpResponseMessage> outcome, int attemptNumber, FundaApiOptions options)
    {
        var response = outcome.Result;

        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta;
            }

            if (retryAfter.Date is { } date && date > DateTimeOffset.UtcNow)
            {
                return date - DateTimeOffset.UtcNow;
            }
        }

        // An HTML response usually means requests are being rejected, and retrying quickly
        // only adds to the load, so back off much longer: with the defaults 30s, 60s, 90s, 120s.
        if (response is not null && IsBlockPage(response))
        {
            return TimeSpan.FromSeconds(options.BlockedRetryDelaySeconds * (attemptNumber + 1));
        }

        // null = use the regular exponential backoff configured above.
        return null;
    }

    private static bool IsRetryable(Outcome<HttpResponseMessage> outcome)
    {
        // Network-level failures (connection reset, DNS, ...).
        if (outcome.Exception is HttpRequestException)
        {
            return true;
        }

        var response = outcome.Result;
        if (response is null)
        {
            return false;
        }

        var status = (int)response.StatusCode;

        // Too many requests, request timeout, server errors.
        if (status is 408 or 429 or >= 500)
        {
            return true;
        }

        return IsBlockPage(response);
    }

    private static bool IsJson(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType?
            .EndsWith("json", StringComparison.OrdinalIgnoreCase) == true;
}
