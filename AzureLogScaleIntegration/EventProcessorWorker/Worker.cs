using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using EventProcessorWorker.Configuration;
using EventProcessorWorker.Services;
using Microsoft.Extensions.Options;

namespace EventProcessorWorker;

[ExcludeFromCodeCoverage]
public class Worker : BackgroundService
{
    private const int MaximumBatchSize = 100;

    private readonly string _instanceId;
    private readonly string _storageAccountName;
    private readonly string _blobContainerName;
    private readonly string _eventHubName;
    private readonly string _eventHubNamespace;
    private readonly EventProcessor<EventProcessorPartition> _processorClient;
    private readonly ILogScaleService _logScaleService;
    private readonly ILogger<Worker> _logger;
    private readonly ILogger<LogScaleEventProcessor> _processorLogger;
    private readonly MD5 _md5Hasher = MD5.Create();
    private readonly Random _random;
    private const int MinProcessingTime = 5;
    private const int MaxProcessingTime = 30;

    public Worker(IOptions<LogScaleProcessorConfiguration> conf, ILogScaleService logScaleService, ILogger<Worker> logger, ILogger<LogScaleEventProcessor> processorLogger)
    {
        var configuration = conf.Value;
        VerifyConfiguration(configuration);
        var replicaName = configuration.ReplicaName ?? "replica";
        _instanceId = replicaName;
        _storageAccountName = configuration.StorageAccountName!;
        _blobContainerName = configuration.BlobContainerName!;
        _eventHubNamespace = configuration.EventHubNamespace!;
        _eventHubName = configuration.EventHubName!;

        _logScaleService = logScaleService;
        _logger = logger;
        _processorLogger = processorLogger;

        _processorClient = GetProcessorPasswordless();

        var hashed = _md5Hasher.ComputeHash(Encoding.UTF8.GetBytes(_instanceId));
        _random = new Random(BitConverter.ToInt32(hashed, 0));
    }

    private void VerifyConfiguration(LogScaleProcessorConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrEmpty(configuration.HumioUrl);
        ArgumentException.ThrowIfNullOrEmpty(configuration.HumioApiKey);
        ArgumentException.ThrowIfNullOrEmpty(configuration.StorageAccountName);
        ArgumentException.ThrowIfNullOrEmpty(configuration.BlobContainerName);
        ArgumentException.ThrowIfNullOrEmpty(configuration.EventHubNamespace);
        ArgumentException.ThrowIfNullOrEmpty(configuration.EventHubName);
    }

    /**
     * Worker locks a partition and processes events for a random number of seconds, then stops processing and unlocks the partition, and restarts.
     */
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing worker {instanceId}", _instanceId);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Start processing for X seconds
            var processingTime = _random.Next(MinProcessingTime, MaxProcessingTime);
            _logger.LogDebug("Processing for {delayTime} seconds", processingTime);

            try
            {
                await _processorClient.StartProcessingAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(processingTime), cancellationToken);
            }
            catch (TaskCanceledException e)
            {
                _logger.LogError("Task cancelled:\n {stackTrace}", e.StackTrace);
                // This is expected if the cancellation token is signaled.
            }
            finally
            {
                // Stop processing
                _logger.LogDebug("Stopping processing");
                await _processorClient.StopProcessingAsync(cancellationToken);
            }
        }

        _logger.LogDebug("Shutting down");
    }

    private LogScaleEventProcessor GetProcessorPasswordless()
    {
        _logger.LogDebug("Creating logscale event processor");

        var azureCredential = new DefaultAzureCredential();

        var blobContainerClient = GetBlobContainerClientPasswordless(azureCredential);

        var processor = new LogScaleEventProcessor(
            MaximumBatchSize,
            EventHubConsumerClient.DefaultConsumerGroupName,
            $"{_eventHubNamespace}.servicebus.windows.net",
            _eventHubName,
            azureCredential,
            blobContainerClient,
            _logScaleService,
            _processorLogger,
            _instanceId,
            new EventProcessorOptions
            {
                LoadBalancingStrategy = LoadBalancingStrategy.Balanced,
                MaximumWaitTime = TimeSpan.FromSeconds(1)
            });

        return processor;
    }

    private BlobContainerClient GetBlobContainerClientPasswordless(DefaultAzureCredential azureCredential)
    {
        return new BlobContainerClient(
            new Uri($"https://{_storageAccountName}.blob.core.windows.net/{_blobContainerName}"),
            azureCredential);
    }
}