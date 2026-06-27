using System.Text.Json;

namespace PocketStation.Infrastructure.Serialization;

public static class PocketJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        WriteIndented = false
    };

    public static JsonElement ToElement(object value) =>
        JsonSerializer.SerializeToElement(value, Options);
}
