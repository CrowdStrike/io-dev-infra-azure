using EventProcessorWorker;
using EventProcessorWorker.Configuration;
using EventProcessorWorker.Services;
using Polly;
using Polly.Extensions.Http;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<ILogScaleService, LogScaleService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(GetRetryPolicy());

builder.Services.AddSingleton<ILogScaleService, LogScaleService>();
builder.Services.AddHostedService<Worker>();
builder.Services.Configure<LogScaleProcessorConfiguration>(builder.Configuration);

var host = builder.Build();
host.Run();
return;

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
        .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2,
            retryAttempt)));
}