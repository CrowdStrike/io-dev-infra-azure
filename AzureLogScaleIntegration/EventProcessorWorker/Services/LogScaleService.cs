using System.Globalization;
using System.Net.Http.Headers;
using EventProcessorWorker.Configuration;
using EventProcessorWorker.Model;
using Microsoft.Extensions.Options;

namespace EventProcessorWorker.Services;

public class LogScaleService : ILogScaleService
{
    private const string? UnstructuredEndpointUri = $"/api/v1/ingest/humio-unstructured";

    private readonly HttpClient _httpClient;

    public LogScaleService(IOptions<LogScaleProcessorConfiguration> conf, HttpClient httpClient)
    {
        var configuration = conf.Value;
        var humioUrl = configuration.HumioUrl + ":443";
        var apikey = configuration.HumioApiKey;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri($"https://{humioUrl}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apikey);
    }

    public async Task<HttpResponseMessage> PushUnstructered(IEnumerable<string> events)
    {
        var dateTime = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        var unstructured = new UnstructuredData
        {
            Fields = new Dictionary<string, string>
            {
                {"eventhub-timestamp", dateTime}
            },
            Messages = events.ToList()
        };

        var httpResponseMessage = await _httpClient.PostAsJsonAsync(UnstructuredEndpointUri, new[] { unstructured });
        return httpResponseMessage.EnsureSuccessStatusCode();
    }
}