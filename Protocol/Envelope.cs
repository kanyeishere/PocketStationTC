using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocketStation.Protocol;

public sealed record Envelope
{
    [JsonPropertyName("v")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("ts")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    public static Envelope Create(string type, object? payload = null) => new()
    {
        Type = type,
        Payload = payload
    };
}

public sealed record IncomingEnvelope
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    public static IncomingEnvelope FromPayload(string type, object payload)
    {
        var doc = JsonSerializer.SerializeToDocument(payload, Plugin.JsonOptions);
        return new IncomingEnvelope { Type = type, Payload = doc.RootElement };
    }
}
