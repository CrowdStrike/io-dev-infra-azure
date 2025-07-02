using System.Net;
using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Storage.Blobs;
using EventProcessorWorker.Services;

namespace EventProcessorWorker;

public class LogScaleEventProcessor : ParallelEventProcessor<EventProcessorPartition>
{
    private readonly string _instanceId;
    private readonly ILogScaleService _logScaleService;
    private readonly ILogger<LogScaleEventProcessor> _logger;

    public LogScaleEventProcessor(
        int eventBatchMaximumCount,
        string consumerGroup,
        string fullyQualifiedNamespace,
        string eventHubName,
        TokenCredential credential,
        BlobContainerClient storageContainer,
        ILogScaleService logScaleService,
        ILogger<LogScaleEventProcessor> logger,
        string instanceId,
        EventProcessorOptions? options = null) : base(eventBatchMaximumCount,
        consumerGroup,
        fullyQualifiedNamespace,
        eventHubName,
        credential,
        storageContainer,
        options)
    {
        _logScaleService = logScaleService;
        _logger = logger;
        _instanceId = instanceId;
    }

    protected override async Task OnProcessingEventBatchAsync(
        IEnumerable<EventData> events,
        EventProcessorPartition partition,
        CancellationToken cancellationToken)
    {
        var eventDataList = events.ToArray();
        if (eventDataList.Length == 0)
        {
            return;
        }

        try
        {
            await ProcessEvents(eventDataList, partition, cancellationToken);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogError("Call unauthorized. May be caused by an invalid or outdated ingest token.");
        }
        catch (Exception e)
        {
            _logger.LogError(@"{exception}", e);
        }
    }

    private async Task ProcessEvents(IEnumerable<EventData> events, EventProcessorPartition partition,
        CancellationToken cancellationToken)
    {
        var eventDatas = events.ToList();
        _logger.LogDebug("{instanceId}: Received batch of {eventsCount} events for partition {partitionId}",
            _instanceId,
            eventDatas.Count,
            partition.PartitionId);

        await _logScaleService.PushUnstructered(eventDatas.Select(x => $"{x.EventBody}"));

        _logger.LogDebug("Writing checkpoint");
        await CheckpointAsync(partition, eventDatas.Last(), cancellationToken);
    }

    protected override Task OnProcessingErrorAsync(
        Exception exception,
        EventProcessorPartition? partition,
        string operationDescription,
        CancellationToken cancellationToken)
    {
        if (partition != null)
        {
            _logger.LogError(
                "{instanceId}: Exception on partition {partitionId} while performing {operationDescription}: {exceptionMessage}",
                _instanceId,
                partition.PartitionId,
                operationDescription,
                exception.Message);
        }
        else
        {
            _logger.LogError("{instanceId}: Exception while performing {operationDescription}: {exceptionMessage}",
                _instanceId,
                operationDescription,
                exception.Message);
        }

        return Task.CompletedTask;
    }
}