using System.Collections.Concurrent;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Domain;

namespace PocketStation.Infrastructure.Network;

public sealed class WebSocketHub
{
    private readonly ConcurrentDictionary<Guid, WebSocketConnection> connections = [];
    private readonly EventBus eventBus;

    public WebSocketHub(EventBus eventBus)
    {
        this.eventBus = eventBus;
    }

    public int Count => connections.Count;

    public bool TryAdd(WebSocketConnection connection, int maxClients)
    {
        if (connections.Count >= maxClients)
            return false;

        return connections.TryAdd(connection.Id, connection);
    }

    public void Remove(WebSocketConnection connection)
    {
        if (connections.TryRemove(connection.Id, out var removed))
            removed.Dispose();
    }

    public Task BroadcastAsync(Envelope envelope)
    {
        return BroadcastRawAsync(eventBus.Serialize(envelope), CancellationToken.None);
    }

    public async Task BroadcastRawAsync(string text, CancellationToken cancellationToken)
    {
        foreach (var connection in connections.Values)
        {
            try
            {
                await connection.SendTextAsync(text, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Dropping failed WebSocket connection {Id}", connection.Id);
                Remove(connection);
            }
        }
    }

    public async Task BroadcastBinaryAsync(byte[] data, CancellationToken cancellationToken)
    {
        foreach (var connection in connections.Values)
        {
            try
            {
                await connection.SendBinaryAsync(data, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Dropping failed WebSocket connection {Id}", connection.Id);
                Remove(connection);
            }
        }
    }
}
