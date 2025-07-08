using System.Globalization;
using System.Net;
using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Storage.Blobs;
using EventProcessorWorker.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace EventProcessorWorker.Test;

public class LogScaleEventProcessorTest
{
    private const string FullyQualifiedNamespace = "namespace";
    private const string EventHubName = "tests";

    private readonly Mock<BlobContainerClient> _mockStorageContainer;
    private readonly Mock<BlobClient> _mockBlobClient;
    private readonly Mock<ILogScaleService> _logScaleService;
    private readonly Mock<ILogger<LogScaleEventProcessor>> _logger;

    private readonly TestableLogScaleEventProcessor _processor;

    public LogScaleEventProcessorTest()
    {
        _mockStorageContainer = new Mock<BlobContainerClient>();
        _mockBlobClient = new Mock<BlobClient>();

        _mockStorageContainer
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockBlobClient.Object);

        _logScaleService = new Mock<ILogScaleService>();
        _logger = new Mock<ILogger<LogScaleEventProcessor>>();

        _processor =
            new TestableLogScaleEventProcessor(_mockStorageContainer.Object, _logScaleService.Object, _logger.Object);
    }

    [Fact]
    public async Task OnProcessingEventBatchAsync_ShouldCheckpoint_WhenEventsExist()
    {
        // Arrange
        var partition = new EventProcessorPartition( );

        var events = new List<EventData>
        {
            new(Array.Empty<byte>()),
            new(Array.Empty<byte>())
        };

        // Act
        await _processor.PublicOnProcessingEventBatchAsync(events, partition, CancellationToken.None);

        // Assert
        _mockBlobClient.Verify(b => b.UploadAsync(
                It.IsAny<Stream>(),
                null,
                It.Is<IDictionary<string, string>>(dictionary =>
                    dictionary["offset"] == events.Last().OffsetString),
                null,
                null,
                null,
                default,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnProcessingErrorAsync_ShouldCatchAndIgnoreExceptions()
    {
        // Arrange
        var partition = new EventProcessorPartition();

        var events = new List<EventData>
        {
            new(Array.Empty<byte>()),
            new(Array.Empty<byte>())
        };

        var exception = new Exception("Test error");

        _logScaleService
            .Setup(service => service.PushUnstructered(It.IsAny<IEnumerable<string>>()))
            .ThrowsAsync(exception);

        // Act
        await _processor.PublicOnProcessingEventBatchAsync(events, partition, CancellationToken.None);

        // Assert
        _logger.Verify(l => l.Log(LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task OnProcessingErrorAsync_ShouldCatchAndIgnoreUnauthorizedExceptions()
    {
        // Arrange
        var partition = new EventProcessorPartition();

        var events = new List<EventData>
        {
            new(Array.Empty<byte>()),
            new(Array.Empty<byte>())
        };

        var exception = new HttpRequestException("Call unauthorized", null, HttpStatusCode.Unauthorized);

        _logScaleService
            .Setup(service => service.PushUnstructered(It.IsAny<IEnumerable<string>>()))
            .ThrowsAsync(exception);

        // Act
        await _processor.PublicOnProcessingEventBatchAsync(events, partition, CancellationToken.None);

        // Assert
        _logger.Verify(l => l.Log(LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Theory, AutoMoqData]
    public async Task OnProcessingErrorAsync_PartitionNotNull_ShouldLogWithPartitionId(
        Exception exception,
        EventProcessorPartition partition,
        string operationDescription)
    {
        // Arrange
        // Act
        await _processor.PublicOnProcessingErrorAsync(exception, partition, operationDescription,
            CancellationToken.None);

        // Assert
        _logger.Verify(l => l.Log(LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Theory, AutoMoqData]
    public async Task OnProcessingErrorAsync_PartitionNull_ShouldLogWithoutPartitionId(
        Exception exception,
        string operationDescription)
    {
        // Arrange
        // Act
        await _processor.PublicOnProcessingErrorAsync(exception, null, operationDescription,
            CancellationToken.None);

        // Assert
        _logger.Verify(l => l.Log(LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    private class TestableLogScaleEventProcessor : LogScaleEventProcessor
    {
        public TestableLogScaleEventProcessor(BlobContainerClient storageContainer,
            ILogScaleService logScaleService,
            ILogger<LogScaleEventProcessor> logger,
            EventProcessorOptions? options = null) : base(100,
            EventHubConsumerClient.DefaultConsumerGroupName,
            LogScaleEventProcessorTest.FullyQualifiedNamespace,
            LogScaleEventProcessorTest.EventHubName,
            new DefaultAzureCredential(),
            storageContainer,
            logScaleService,
            logger,
            "id",
            options)
        {
        }

        public Task PublicOnProcessingEventBatchAsync(IEnumerable<EventData> events, EventProcessorPartition partition,
            CancellationToken cancellationToken)
        {
            return OnProcessingEventBatchAsync(events, partition, cancellationToken);
        }

        public Task PublicOnProcessingErrorAsync(Exception exception, EventProcessorPartition? partition,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            return OnProcessingErrorAsync(exception, partition, operationDescription, cancellationToken);
        }
    }
}