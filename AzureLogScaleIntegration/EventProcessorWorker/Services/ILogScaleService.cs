namespace EventProcessorWorker.Services;

public interface ILogScaleService
{
    /**
     * Calls unstructured ingest endpoint.
     * <see href="https://library.humio.com/logscale-api/api-ingest-parser.html"/>
     */
    public Task<HttpResponseMessage> PushUnstructered(IEnumerable<string> events);
}