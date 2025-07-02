using System.Globalization;
using Azure;
using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace EventProcessorWorker;

public abstract class ParallelEventProcessor<TPartition> : EventProcessor<TPartition>
    where TPartition : EventProcessorPartition, new()
{
    public const string OwnershipPrefixFormat = "{0}/{1}/{2}/ownership/";
    public const string OwnerIdentifierMetadataKey = "ownerid";
    public const string CheckpointPrefixFormat = "{0}/{1}/{2}/checkpoint/";
    public const string CheckpointBlobNameFormat = "{0}/{1}/{2}/checkpoint/{3}";
    public const string OffsetMetadataKey = "offset";

    private BlobContainerClient StorageContainer { get; }

    protected ParallelEventProcessor(int eventBatchMaximumCount,
        string consumerGroup,
        string fullyQualifiedNamespace,
        string eventHubName,
        TokenCredential credential,
        BlobContainerClient storageContainer,
        EventProcessorOptions? options = null) : base(eventBatchMaximumCount,
        consumerGroup,
        fullyQualifiedNamespace,
        eventHubName,
        credential,
        options)
    {
        StorageContainer = storageContainer;
    }

    protected override async Task<IEnumerable<EventProcessorPartitionOwnership>> ListOwnershipAsync(
        CancellationToken cancellationToken)
    {
        var partitionOwnerships = new List<EventProcessorPartitionOwnership>();

        var ownershipBlobsPrefix = string.Format(
            OwnershipPrefixFormat,
            FullyQualifiedNamespace.ToLowerInvariant(),
            EventHubName.ToLowerInvariant(),
            ConsumerGroup.ToLowerInvariant());

        var configuredCancelableAsyncEnumerable = StorageContainer.GetBlobsAsync(
            traits: BlobTraits.Metadata,
            prefix: ownershipBlobsPrefix,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await foreach (var blob in configuredCancelableAsyncEnumerable)
        {
            partitionOwnerships.Add(new EventProcessorPartitionOwnership
            {
                ConsumerGroup = ConsumerGroup,
                EventHubName = EventHubName,
                FullyQualifiedNamespace = FullyQualifiedNamespace,
                LastModifiedTime = blob.Properties.LastModified.GetValueOrDefault(),
                OwnerIdentifier = blob.Metadata[OwnerIdentifierMetadataKey],
                PartitionId = blob.Name.Substring(ownershipBlobsPrefix.Length),
                Version = blob.Properties.ETag.ToString()
            });
        }

        return partitionOwnerships;
    }

    protected override async Task<IEnumerable<EventProcessorPartitionOwnership>> ClaimOwnershipAsync(
        IEnumerable<EventProcessorPartitionOwnership> desiredOwnership,
        CancellationToken cancellationToken)
    {
        var claimedOwnerships = new List<EventProcessorPartitionOwnership>();

        foreach (var ownership in desiredOwnership)
        {
            var ownershipMetadata = new Dictionary<string, string>
            {
                { OwnerIdentifierMetadataKey, ownership.OwnerIdentifier }
            };

            // Construct the path to the blob and get a blob client for it, so we can interact with it.
            var ownershipBlob = string.Format(
                OwnershipPrefixFormat + ownership.PartitionId,
                ownership.FullyQualifiedNamespace.ToLowerInvariant(),
                ownership.EventHubName.ToLowerInvariant(),
                ownership.ConsumerGroup.ToLowerInvariant());
            var ownershipBlobClient = StorageContainer.GetBlobClient(ownershipBlob);

            try
            {
                if (ownership.Version == null)
                {
                    // In this case, we are trying to claim ownership of a partition which was previously unowned, and hence did not have an ownership file. To ensure only a single host grabs the partition,
                    // we use a conditional request so that we only create our blob in the case where it does not yet exist.
                    var requestConditions = new BlobRequestConditions { IfNoneMatch = ETag.All };

                    using var emptyStream = new MemoryStream([]);
                    BlobContentInfo info = await ownershipBlobClient.UploadAsync(
                        emptyStream,
                        metadata: ownershipMetadata,
                        conditions: requestConditions,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    claimedOwnerships.Add(new EventProcessorPartitionOwnership
                    {
                        ConsumerGroup = ownership.ConsumerGroup,
                        EventHubName = ownership.EventHubName,
                        FullyQualifiedNamespace = ownership.FullyQualifiedNamespace,
                        LastModifiedTime = info.LastModified,
                        OwnerIdentifier = ownership.OwnerIdentifier,
                        PartitionId = ownership.PartitionId,
                        Version = info.ETag.ToString()
                    });
                }
                else
                {
                    // In this case, the partition is owned by some other host. The ownership file already exists, so we just need to change metadata on it. But we should only do this if the metadata has not
                    // changed between when we listed ownership and when we are trying to claim ownership, i.e. the ETag for the file has not changed.
                    var requestConditions = new BlobRequestConditions
                        { IfMatch = new ETag(ownership.Version) };
                    BlobInfo info = await ownershipBlobClient
                        .SetMetadataAsync(ownershipMetadata, requestConditions, cancellationToken)
                        .ConfigureAwait(false);

                    claimedOwnerships.Add(new EventProcessorPartitionOwnership
                    {
                        ConsumerGroup = ownership.ConsumerGroup,
                        EventHubName = ownership.EventHubName,
                        FullyQualifiedNamespace = ownership.FullyQualifiedNamespace,
                        LastModifiedTime = info.LastModified,
                        OwnerIdentifier = ownership.OwnerIdentifier,
                        PartitionId = ownership.PartitionId,
                        Version = info.ETag.ToString()
                    });
                }
            }
            catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobAlreadyExists ||
                                                   e.ErrorCode == BlobErrorCode.ConditionNotMet)
            {
                // In this case, another host has claimed the partition before we did. That's safe to ignore. We'll still try to claim other partitions.
            }
        }

        return claimedOwnerships;
    }

    protected override async Task<IEnumerable<EventProcessorCheckpoint>> ListCheckpointsAsync(
        CancellationToken cancellationToken)
    {
        var checkpoints = new List<EventProcessorCheckpoint>();
        var checkpointBlobsPrefix = string.Format(CheckpointPrefixFormat, FullyQualifiedNamespace.ToLowerInvariant(),
            EventHubName.ToLowerInvariant(), ConsumerGroup.ToLowerInvariant());

        await foreach (var item in StorageContainer.GetBlobsAsync(traits: BlobTraits.Metadata,
                           prefix: checkpointBlobsPrefix, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (item.Metadata.TryGetValue(OffsetMetadataKey,
                    out var readOnlySpan) &&
                long.TryParse(readOnlySpan,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var offset))
            {
                checkpoints.Add(new EventProcessorCheckpoint
                {
                    ConsumerGroup = ConsumerGroup,
                    EventHubName = EventHubName,
                    FullyQualifiedNamespace = FullyQualifiedNamespace,
                    PartitionId = item.Name.Substring(checkpointBlobsPrefix.Length),
                    StartingPosition = EventPosition.FromOffset(offset, isInclusive: false)
                });
            }
        }

        return checkpoints;
    }

    protected override async Task<EventProcessorCheckpoint> GetCheckpointAsync(string partitionId,
        CancellationToken cancellationToken)
    {
        var checkpointName = string.Format(CheckpointBlobNameFormat,
            FullyQualifiedNamespace.ToLowerInvariant(),
            EventHubName.ToLowerInvariant(),
            ConsumerGroup.ToLowerInvariant(),
            partitionId);

        try
        {
            BlobProperties properties = await StorageContainer.GetBlobClient(checkpointName)
                .GetPropertiesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (long.TryParse(properties.Metadata[OffsetMetadataKey], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var offset))
            {
                return new EventProcessorCheckpoint
                {
                    ConsumerGroup = ConsumerGroup,
                    EventHubName = EventHubName,
                    FullyQualifiedNamespace = FullyQualifiedNamespace,
                    PartitionId = partitionId,
                    StartingPosition = EventPosition.FromOffset(offset, isInclusive: false)
                };
            }
        }
        catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            // There's no checkpoint for this partition yet, but that's okay, so we ignore this exception.
        }

        return null!;
    }

    protected async Task CheckpointAsync(TPartition partition, EventData data,
        CancellationToken cancellationToken = default)
    {
        var checkpointBlob = string.Format(CheckpointPrefixFormat + partition.PartitionId,
            FullyQualifiedNamespace.ToLowerInvariant(),
            EventHubName.ToLowerInvariant(),
            ConsumerGroup.ToLowerInvariant());
        var checkpointMetadata = new Dictionary<string, string>
        {
            { OffsetMetadataKey, data.Offset.ToString(CultureInfo.InvariantCulture) }
        };

        using var emptyStream = new MemoryStream([]);
        var blobClient = StorageContainer.GetBlobClient(checkpointBlob);
        var uploadAsync = blobClient.UploadAsync(emptyStream,
            metadata: checkpointMetadata,
            cancellationToken: cancellationToken);
        await uploadAsync
            .ConfigureAwait(false);
    }
}