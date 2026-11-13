using System.Threading.RateLimiting;

namespace FundaAssignment.Infrastructure.Http;

/// <summary>
/// Spaces outgoing requests evenly so the API's request limit is never approached.
/// </summary>
internal sealed class RateLimitingHandler(RateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Waits (without blocking a thread) until a permit is available.
        using var lease = await limiter.AcquireAsync(permitCount: 1, cancellationToken);

        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("The rate limiter did not grant a permit.");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}