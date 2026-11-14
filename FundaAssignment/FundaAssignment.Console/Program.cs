using FundaAssignment.Console.Reporting;
using FundaAssignment.Core.Interfaces;
using FundaAssignment.Core.Services;
using FundaAssignment.Infrastructure;
using FundaAssignment.Infrastructure.Api.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddFundaApi(builder.Configuration);
builder.Services.AddTransient<TopAgentsService>();
builder.Services.AddSingleton<IRankingWriter>(_ => new ConsoleTableWriter(Console.Out));

using var host = builder.Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    // Resolving the services binds and validates the Funda options, so bad configuration fails here.
    var service = host.Services.GetRequiredService<TopAgentsService>();
    var writer = host.Services.GetRequiredService<IRankingWriter>();

    foreach (var report in Reports.All)
    {
        var rankings = await service.GetTopAgentsAsync(report.Search, count: 10, cts.Token);
        writer.Write(report.Title, rankings);
    }

    return 0;
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Invalid configuration:");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    return 3;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (OperationCanceledException)
{
    // Not cancelled by the user, so the HttpClient timeout ran out.
    Console.Error.WriteLine(
        "The Funda API did not respond in time, even after retrying. Check your connection and try again.");
    return 1;
}
catch (FundaApiBlockedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"The Funda API request failed: {ex.Message}");
    return 1;
}
