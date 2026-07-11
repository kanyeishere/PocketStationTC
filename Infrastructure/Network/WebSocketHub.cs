using System.Collections.Concurrent;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Domain;

namespace PocketStation.Infrastructure.Network;

public sealed class WebSocketHub
{
    private static readonly TimeSpan TextSendTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BinarySendTimeout = TimeSpan.FromSeconds(2);

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

    public Task BroadcastRawAsync(string text, CancellationToken cancellationToken)
    {
        var snapshot = connections.Values.ToArray();
        return Task.WhenAll(snapshot.Select(connection =>
            SendWithTimeoutAsync(
                connection,
                token => connection.SendTextAsync(text, token),
                TextSendTimeout,
                "text",
                cancellationToken)));
    }

    public Task BroadcastBinaryAsync(byte[] data, CancellationToken cancellationToken)
    {
        var snapshot = connections.Values.ToArray();
        return Task.WhenAll(snapshot.Select(connection =>
            SendWithTimeoutAsync(
                connection,
                token => connection.SendBinaryAsync(data, token),
                BinarySendTimeout,
                "binary",
                cancellationToken)));
    }

    private async Task SendWithTimeoutAsync(
        WebSocketConnection connection,
        Func<CancellationToken, Task> send,
        TimeSpan timeout,
        string kind,
        CancellationToken cancellationToken)
    {
        var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sendTask = send(sendCts.Token);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        try
        {
            var completed = await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(false);
            if (completed != sendTask)
            {
                await sendCts.CancelAsync().ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    ObserveDetachedSend(sendTask, sendCts);
                    throw new OperationCanceledException(cancellationToken);
                }

                Plugin.Log.Debug("Dropping slow WebSocket connection {Id} after {Kind} send timeout of {TimeoutMs}ms",
                    connection.Id, kind, (int)timeout.TotalMilliseconds);
                Remove(connection);
                ObserveDetachedSend(sendTask, sendCts);
                return;
            }

            await sendTask.ConfigureAwait(false);
            sendCts.Dispose();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sendCts.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            sendCts.Dispose();
            Plugin.Log.Debug(ex, "Dropping failed WebSocket connection {Id} during {Kind} send", connection.Id, kind);
            Remove(connection);
        }
    }

    private static void ObserveDetachedSend(Task sendTask, CancellationTokenSource sendCts)
    {
        _ = sendTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                _ = t.Exception;

            sendCts.Dispose();
        }, TaskScheduler.Default);
    }
}
