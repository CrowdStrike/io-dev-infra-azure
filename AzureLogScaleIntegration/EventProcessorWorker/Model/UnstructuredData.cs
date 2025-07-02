using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EventProcessorWorker.Model;

[ExcludeFromCodeCoverage]
public record UnstructuredData
{
    [JsonPropertyName("messages")]
    public List<string> Messages { get; set; } = [];

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, string>? Fields { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}