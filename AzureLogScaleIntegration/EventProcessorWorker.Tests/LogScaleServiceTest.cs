using EventProcessorWorker.Configuration;
using EventProcessorWorker.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Contrib.HttpClient;

namespace EventProcessorWorker.Test;

public class LogScaleServiceTest
{
    [Theory, AutoMoqData]
    public async Task PushUnstructuredTest(
        Mock<HttpMessageHandler> handler,
        Mock<IOptions<LogScaleProcessorConfiguration>> configuration,
        IEnumerable<string> events,
        string humioUri,
        string humioApiKey)
    {
        configuration.Setup(x => x.Value).Returns(new LogScaleProcessorConfiguration()
        {
            HumioUrl = humioUri,
            HumioApiKey = humioApiKey
        });

        handler.SetupRequest(HttpMethod.Post, "https://localhost/api/v1/ingest/humio-unstructured")
            .ReturnsResponse("Success");

        var client = handler.CreateClient();

        var sut = new LogScaleService(configuration.Object, client);

        var httpResponseMessage = await sut.PushUnstructered(events);

        httpResponseMessage.Should().BeSuccessful();
        handler.VerifyAnyRequest();
    }
}