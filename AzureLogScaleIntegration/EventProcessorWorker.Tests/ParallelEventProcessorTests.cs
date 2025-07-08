using System.Globalization;
using Azure;
using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Moq;

namespace EventProcessorWorker.Test;

public class ParallelEventProcessorTests
{
    private const string FullyQualifiedNamespace = "namespace";
    private const string EventHubName = "tests";

    private static string consumerGroupName = EventHubConsumerClient.DefaultConsumerGroupName.ToLowerInvariant();

    private readonly string _expectedCheckpointBlobPath = string.Format(
        TestableParallelEventProcessor.CheckpointPrefixFormat,
        FullyQualifiedNamespace,
        EventHubName,
        consumerGroupName);

    private readonly string _expectedOwnershipBlobPath = string.Format(
        TestableParallelEventProcessor.OwnershipPrefixFormat,
        FullyQualifiedNamespace,
        EventHubName,
        consumerGroupName);

    private readonly Mock<BlobContainerClient> _mockStorageContainer;
    private readonly Mock<BlobClient> _mockBlobClient;
    private readonly TestableParallelEventProcessor _processor;

    public ParallelEventProcessorTests()
    {
        _mockStorageContainer = new Mock<BlobContainerClient>();
        _mockBlobClient = new Mock<BlobClient>();

        _mockStorageContainer
            .Setup(c => c.GetBlobClient(_expectedCheckpointBlobPath))
            .Returns(_mockBlobClient.Object);

        _processor = new TestableParallelEventProcessor(_mockStorageContainer.Object);
    }

    [Theory, AutoMoqData]
    public async Task CheckpointAsync_UploadsToBlob_WithCorrectMetadata(
        EventData eventData)
    {
        // Arrange
        var partition = new EventProcessorPartition();

        var expectedMetadata = new Dictionary<string, string>
        {
            {
                TestableParallelEventProcessor.OffsetMetadataKey,
                eventData.OffsetString
            }
        };

        // Act
        await _processor.PublicCheckpointAsync(partition, eventData);

        // Assert
        _mockBlobClient.Verify(b => b.UploadAsync(
                It.IsAny<Stream>(),
                null,
                expectedMetadata,
                null,
                null,
                null,
                default,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory, AutoMoqData]
    public async Task CheckpointAsync_HandlesException_DuringUpload(EventData eventData)
    {
        // Arrange
        var partition = new EventProcessorPartition();

        _mockBlobClient.Setup(b => b.UploadAsync(It.IsAny<Stream>(),
                null,
                It.IsAny<IDictionary<string, string>>(),
                null,
                null,
                null,
                default,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Upload failed"));

        // Act
        var action = async () => await _processor.PublicCheckpointAsync(partition, eventData);

        // Assert
        await action.Should().ThrowAsync<Exception>().WithMessage("Upload failed");
    }

    [Theory, AutoMoqData]
    public async Task ClaimOwnershipAsync_ShouldClaimUnownedPartition_WhenVersionIsNull(Response response,
        EventProcessorPartitionOwnership partitionOwnership)
    {
        // Arrange
        partitionOwnership.Version = null;

        var blobContentInfo =
            BlobsModelFactory.BlobContentInfo(ETag.All, DateTimeOffset.UtcNow, [], "1", 1L);

        var fromValue = Response.FromValue(blobContentInfo, response);

        _mockBlobClient.Setup(b => b.UploadAsync(It.IsAny<Stream>(),
                null,
                It.IsAny<IDictionary<string, string>>(),
                It.Is<BlobRequestConditions>(c => c.IfNoneMatch == ETag.All),
                null,
                null,
                default,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromValue);

        _mockStorageContainer
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockBlobClient.Object);

        // Act
        var result = (await _processor.PublicClaimOwnershipAsync([partitionOwnership], CancellationToken.None))
            .ToList();

        // Assert
        result.Should().ContainSingle();
        var claimedOwnership = result.First();

        claimedOwnership.PartitionId.Should().Be(partitionOwnership.PartitionId);
        claimedOwnership.OwnerIdentifier.Should().Be(partitionOwnership.OwnerIdentifier);
        claimedOwnership.Version.Should().Be(blobContentInfo.ETag.ToString());
    }

    [Theory, AutoMoqData]
    public async Task ClaimOwnershipAsync_ShouldUpdateMetadata_WhenVersionIsNotNull(Response response,
        EventProcessorPartitionOwnership partitionOwnership)
    {
        // Arrange
        partitionOwnership.Version = "oldETag";

        var blobContentInfo =
            BlobsModelFactory.BlobInfo(ETag.All, DateTimeOffset.UtcNow);

        var fromValue = Response.FromValue(blobContentInfo, response);

        _mockBlobClient.Setup(b => b.SetMetadataAsync(
                It.IsAny<IDictionary<string, string>>(),
                It.Is<BlobRequestConditions>(c => c.IfMatch == new ETag(partitionOwnership.Version)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromValue);

        _mockStorageContainer
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockBlobClient.Object);

        // Act
        var result = (await _processor.PublicClaimOwnershipAsync(new[] { partitionOwnership }, CancellationToken.None))
            .ToList();

        // Assert
        result.Should().ContainSingle();
        var claimedOwnership = result.First();

        claimedOwnership.PartitionId.Should().Be(partitionOwnership.PartitionId);
        claimedOwnership.OwnerIdentifier.Should().Be(partitionOwnership.OwnerIdentifier);
        claimedOwnership.Version.Should().Be(blobContentInfo.ETag.ToString());
    }


    [Theory, AutoMoqData]
    public async Task ClaimOwnershipAsync_ShouldIgnoreBlobAlreadyExistsError(
        EventProcessorPartitionOwnership partitionOwnership)
    {
        // Arrange
        partitionOwnership.Version = null;

        _mockBlobClient.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobHttpHeaders>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobRequestConditions>(),
                null,
                null,
                default,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(0, BlobErrorCode.BlobAlreadyExists.ToString(),
                BlobErrorCode.BlobAlreadyExists.ToString(), null));

        _mockStorageContainer
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockBlobClient.Object);

        // Act
        var result = await _processor.PublicClaimOwnershipAsync([partitionOwnership], CancellationToken.None);

        // Assert
        result.Should().BeEmpty("ownership was already claimed by another host");
    }

    [Fact]
    public async Task ListOwnershipAsync_ShouldReturnCorrectOwnerships()
    {
        // Arrange
        var blobItems = new[]
        {
            BlobsModelFactory.BlobItem(
                name: $"{_expectedOwnershipBlobPath}partition1",
                metadata: new Dictionary<string, string> { { "ownerid", "owner1" } },
                properties: BlobsModelFactory.BlobItemProperties(
                    true,
                    lastModified: DateTimeOffset.UtcNow,
                    eTag: new ETag("etag1"))),
            BlobsModelFactory.BlobItem(
                name: $"{_expectedOwnershipBlobPath}partition2",
                metadata: new Dictionary<string, string> { { "ownerid", "owner2" } },
                properties: BlobsModelFactory.BlobItemProperties(
                    true,
                    lastModified: DateTimeOffset.UtcNow,
                    eTag: new ETag("etag2")))
        };

        _mockStorageContainer.Setup(container => container.GetBlobsAsync(
                BlobTraits.Metadata,
                BlobStates.None,
                _expectedOwnershipBlobPath,
                It.IsAny<CancellationToken>()))
            .Returns(BlobItemPage(blobItems));

        // Act
        var result = (await _processor.PublicListOwnershipAsync(CancellationToken.None)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainSingle(o => o.PartitionId == "partition1" && o.OwnerIdentifier == "owner1");
        result.Should().ContainSingle(o => o.PartitionId == "partition2" && o.OwnerIdentifier == "owner2");
    }

    [Fact]
    public async Task ListCheckpointsAsync_ShouldReturnCorrectCheckpoints()
    {
        // Arrange
        var blobItems = new[]
        {
            BlobsModelFactory.BlobItem(
                name: $"{_expectedCheckpointBlobPath}partition1",
                metadata: new Dictionary<string, string> { { "offset", "100" } },
                properties: BlobsModelFactory.BlobItemProperties(true)),
            BlobsModelFactory.BlobItem(
                name: $"{_expectedCheckpointBlobPath}partition2",
                metadata: new Dictionary<string, string> { { "offset", "200" } },
                properties: BlobsModelFactory.BlobItemProperties(
                    true))
        };

        _mockStorageContainer.Setup(container => container.GetBlobsAsync(
                BlobTraits.Metadata,
                BlobStates.None,
                _expectedCheckpointBlobPath,
                It.IsAny<CancellationToken>()))
            .Returns(BlobItemPage(blobItems));

        // Act
        var result = await _processor.PublicListCheckpointsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainSingle(o =>
            o.PartitionId == "partition1" && o.StartingPosition == EventPosition.FromOffset("100", false));
        result.Should().ContainSingle(o =>
            o.PartitionId == "partition2" && o.StartingPosition == EventPosition.FromOffset("200", false));
    }

    [Fact]
    public async Task ListCheckpointsAsync_ShouldSkipMissingOffsetMetadata()
    {
        // Arrange
        var blobItems = new[]
        {
            BlobsModelFactory.BlobItem(
                name: $"{_expectedCheckpointBlobPath}partition2",
                metadata: new Dictionary<string, string> { }, // Missing offset
                properties: BlobsModelFactory.BlobItemProperties(true))
        };

        _mockStorageContainer.Setup(container => container.GetBlobsAsync(
                BlobTraits.Metadata,
                BlobStates.None,
                _expectedCheckpointBlobPath,
                It.IsAny<CancellationToken>()))
            .Returns(BlobItemPage(blobItems));

        // Act
        var result = await _processor.PublicListCheckpointsAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory, AutoMoqData]
    public async Task GetCheckpointAsync_ShouldReturnCheckpoint_WhenBlobExists(Response response, string partitionId,
        string expectedOffset)
    {
        // Arrange
        var fromValue = Response.FromValue(BlobsModelFactory.BlobProperties(metadata: new Dictionary<string, string>
        {
            { "offset", expectedOffset.ToString(CultureInfo.InvariantCulture) }
        }), response);

        _mockBlobClient
            .Setup(blob => blob.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fromValue);

        _mockStorageContainer
            .Setup(container => container.GetBlobClient(_expectedCheckpointBlobPath + partitionId))
            .Returns(_mockBlobClient.Object);

        // Act
        var checkpoint = await _processor.PublicGetCheckpointAsync(partitionId, CancellationToken.None);

        // Assert
        checkpoint.Should().NotBeNull();
        checkpoint.PartitionId.Should().Be(partitionId);
        checkpoint.StartingPosition.Should().Be(EventPosition.FromOffset(expectedOffset, false));
        checkpoint.ConsumerGroup.Should().Be(consumerGroupName);
        checkpoint.EventHubName.Should().Be(EventHubName);
        checkpoint.FullyQualifiedNamespace.Should().Be(FullyQualifiedNamespace);
    }

    [Theory, AutoMoqData]
    public async Task GetCheckpointAsync_ShouldReturnNull_WhenBlobNotFound(string partitionId)
    {
        // Arrange
        _mockBlobClient
            .Setup(blob => blob.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, BlobErrorCode.BlobNotFound.ToString(),
                BlobErrorCode.BlobNotFound.ToString(), null));

        _mockStorageContainer
            .Setup(container => container.GetBlobClient(_expectedCheckpointBlobPath + partitionId))
            .Returns(_mockBlobClient.Object);

        // Act
        var checkpoint = await _processor.PublicGetCheckpointAsync(partitionId, CancellationToken.None);

        // Assert
        checkpoint.Should().BeNull();
    }

    private class TestableParallelEventProcessor(BlobContainerClient storageContainer)
        : ParallelEventProcessor<EventProcessorPartition>(100,
            consumerGroupName,
            ParallelEventProcessorTests.FullyQualifiedNamespace,
            ParallelEventProcessorTests.EventHubName,
            new DefaultAzureCredential(),
            storageContainer)
    {
        protected override Task OnProcessingEventBatchAsync(
            IEnumerable<EventData> events,
            EventProcessorPartition partition,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        protected override Task OnProcessingErrorAsync(
            Exception exception,
            EventProcessorPartition partition,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task PublicCheckpointAsync(EventProcessorPartition partition, EventData data,
            CancellationToken cancellationToken = default)
        {
            return CheckpointAsync(partition, data, cancellationToken);
        }

        public Task<IEnumerable<EventProcessorPartitionOwnership>> PublicClaimOwnershipAsync(
            IEnumerable<EventProcessorPartitionOwnership> desiredOwnership,
            CancellationToken cancellationToken)
        {
            return ClaimOwnershipAsync(desiredOwnership, cancellationToken);
        }

        public Task<IEnumerable<EventProcessorPartitionOwnership>> PublicListOwnershipAsync(
            CancellationToken cancellationToken)
        {
            return ListOwnershipAsync(cancellationToken);
        }

        public Task<IEnumerable<EventProcessorCheckpoint>> PublicListCheckpointsAsync(
            CancellationToken cancellationToken)
        {
            return ListCheckpointsAsync(cancellationToken);
        }

        public Task<EventProcessorCheckpoint> PublicGetCheckpointAsync(string partitionId,
            CancellationToken cancellationToken)
        {
            return base.GetCheckpointAsync(partitionId, cancellationToken);
        }
    }

    private static AsyncPageable<BlobItem> BlobItemPage(params BlobItem[] items) =>
        AsyncPageable<BlobItem>.FromPages(new[]
        {
            Page<BlobItem>.FromValues(items, null, Mock.Of<Response>())
        });
}