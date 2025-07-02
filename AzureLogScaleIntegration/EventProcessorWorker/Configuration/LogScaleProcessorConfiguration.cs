using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EventProcessorWorker.Configuration;

[ExcludeFromCodeCoverage]
public class LogScaleProcessorConfiguration
{
    [JsonPropertyName("HumioUrl")]
    public string? HumioUrl { get; set; } = null;

    [JsonPropertyName("HumioApiKey")]
    public string? HumioApiKey { get; set; } = null;

    [JsonPropertyName("storageAccountName")]
    public string? StorageAccountName { get; set; } = null;

    [JsonPropertyName("blobContainerName")]
    public string? BlobContainerName { get; set; } = null;

    [JsonPropertyName("storageAccountName")]
    public string? EventHubNamespace { get; set; } = null;

    [JsonPropertyName("storageAccountName")]
    public string? EventHubName { get; set; } = null;

    [JsonPropertyName("CONTAINER_APP_REPLICA_NAME")]
    public string? ReplicaName { get; set; }

}