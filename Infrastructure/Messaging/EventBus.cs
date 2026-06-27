using System.Text.Json;
using PocketStation;
using PocketStation.Domain;
using PocketStation.Infrastructure.Serialization;

namespace PocketStation.Infrastructure.Messaging;

public sealed class EventBus
{
    public event Func<Envelope, Task>? Published;

    public void Publish(string type, object? payload = null)
    {
        var envelope = Envelope.Create(type, payload);
        var handlers = Published;
        if (handlers == null)
            return;

        foreach (Func<Envelope, Task> handler in handlers.GetInvocationList())
            _ = SafeInvokeAsync(handler, envelope);
    }

    public string Serialize(Envelope envelope) => JsonSerializer.Serialize(envelope, PocketJson.Options);

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
