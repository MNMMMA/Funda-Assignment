using System.Net;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using FundaAssignment.Core.Interfaces;
using FundaAssignment.Infrastructure.Api;
using FundaAssignment.Infrastructure.Http;
using FundaAssignment.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace FundaAssignment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFundaApi(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(FundaApiOptions.SectionName);

        services.AddOptions<FundaApiOptions>()
            .Bind(section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Funda:ApiKey is not configured.")
            .Validate(o => o.PageSize is > 0 and <= FundaApiOptions.MaxPageSize,
                $"Funda:PageSize must be between 1 and {FundaApiOptions.MaxPageSize}.")
            .Validate(o => o.RequestsPerMinute is > 0 and <= 90,
                "Funda:RequestsPerMinute must be between 1 and 90.")
            .Validate(o => o.MaxRetryAttempts >= 0, "Funda:MaxRetryAttempts cannot be negative.")
            .Validate(o => o.RetryBaseDelaySeconds > 0, "Funda:RetryBaseDelaySeconds must be positive.")
            .Validate(o => o.BlockedRetryDelaySeconds > 0, "Funda:BlockedRetryDelaySeconds must be positive.")
            .ValidateOnStart();

        // One shared limiter for the whole process, so spacing holds across handler recycling.
        services.AddSingleton<RateLimiter>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<FundaApiOptions>>().Value;

            // A bucket of one token refilled evenly = one request every 60/N seconds.
            return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / o.RequestsPerMinute),
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
        });
        services.AddTransient<RateLimitingHandler>();

        var retryOptions = section.Get<FundaApiOptions>() ?? new FundaApiOptions();

        var httpClientBuilder = services
            .AddHttpClient<IListingSource, FundaApiClient>((sp, client) =>
            {
                var o = sp.GetRequiredService<IOptions<FundaApiOptions>>().Value;
                client.BaseAddress = new Uri($"{o.BaseUrl.TrimEnd('/')}/{o.ApiKey}/");

                // The timeout covers the whole pipeline, including rate-limit waits and retry delays
                // (block-page retries alone take 30+60+90+120s with the defaults).
                client.Timeout = TimeSpan.FromMinutes(10);

                client.DefaultRequestHeaders.UserAgent.ParseAdd("FundaAssignment/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
                client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

        // Handlers nest in registration order: the first one added is the outermost.
        // Retry wraps the rate limiter, so every retry attempt is throttled too.
        // Polly rejects MaxRetryAttempts = 0, so "no retries" means no retry handler at all.
        if (retryOptions.MaxRetryAttempts > 0)
        {
            httpClientBuilder.AddResilienceHandler("funda-retry", (builder, context) =>
            {
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(FundaRetryPolicy).FullName!);
                builder.AddRetry(FundaRetryPolicy.Create(retryOptions, logger));
            });
        }

        httpClientBuilder.AddHttpMessageHandler<RateLimitingHandler>();

        return services;
    }
}