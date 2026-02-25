using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient<INetSuiteBusinessAppClient, NetSuiteBusinessAppClient>(client =>
{
    var baseUrl = Environment.GetEnvironmentVariable("NETSUITE_BUSINESSAPP_BASEURL")
                  ?? throw new InvalidOperationException("NETSUITE_BUSINESSAPP_BASEURL is required.");
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add(
        "x-functions-key",
        Environment.GetEnvironmentVariable("NETSUITE_BUSINESSAPP_KEY")
            ?? throw new InvalidOperationException("NETSUITE_BUSINESSAPP_KEY is required."));
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
