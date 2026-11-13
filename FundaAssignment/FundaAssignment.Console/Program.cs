using FundaAssignment.Console;
using FundaAssignment.Core.Interfaces;
using FundaAssignment.Core.Services;
using FundaAssignment.Infrastructure;
using FundaAssignment.Infrastructure.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

var service = host.Services.GetRequiredService<TopAgentsService>();
var writer = host.Services.GetRequiredService<IRankingWriter>();

try
{
    foreach (var report in Reports.All)
    {
        var rankings = await service.GetTopAgentsAsync(report.Search, count: 10, cts.Token);
        writer.Write(report.Title, rankings);
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
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
