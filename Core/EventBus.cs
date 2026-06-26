using System.Text.Json;
using PocketStation.Protocol;

namespace PocketStation.Core;

public sealed class EventBus
{
    public event Func<Envelope, Task>? Published;

    public JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public void Publish(string type, object? payload = null)
    {
        var envelope = Envelope.Create(type, payload);
        var handlers = Published;
        if (handlers == null)
            return;

        foreach (Func<Envelope, Task> handler in handlers.GetInvocationList())
            _ = SafeInvokeAsync(handler, envelope);
    }

    public string Serialize(Envelope envelope) => JsonSerializer.Serialize(envelope, JsonOptions);

    private static async Task SafeInvokeAsync(Func<Envelope, Task> handler, Envelope envelope)
    {
        try
        {
            await handler(envelope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Event bus subscriber failed for {Type}", envelope.Type);
        }
    }
}
