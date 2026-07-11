using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketStation.Api;
using PocketStation.Domain;
using PocketStation.Host;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Infrastructure.Network;
using PocketStation.Infrastructure.Serialization;
using PocketStation.Services;

namespace PocketStation.Api;

public sealed class WebSocketHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleConnectionTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PingSendTimeout = TimeSpan.FromSeconds(5);

    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly WebSocketHub webSocketHub;
    private readonly CommandDispatcher commandDispatcher;
    private readonly ChatMonitorModule chatMonitor;
    private readonly PlayerStateModule playerState;

    public WebSocketHandler(
        Configuration configuration,
        EventBus eventBus,
        WebSocketHub webSocketHub,
        CommandDispatcher commandDispatcher,
        ChatMonitorModule chatMonitor,
        PlayerStateModule playerState)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        this.webSocketHub = webSocketHub;
        this.commandDispatcher = commandDispatcher;
        this.chatMonitor = chatMonitor;
        this.playerState = playerState;
    }

    public async Task HandleWebSocketAsync(
        TcpClient client,
        NetworkStream stream,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!HttpHelpers.IsAuthorized(request, configuration.RequireToken ? "1" : null, configuration.Token))
        {
            await HttpHelpers.WriteResponseAsync(stream, 401, "application/json",
                HttpHelpers.JsonBytes(new { ok = false, error = "Unauthorized" }), ct).ConfigureAwait(false);
            client.Dispose();
            return;
        }

        if (webSocketHub.Count >= configuration.MaxClients)
        {
            await HttpHelpers.WriteResponseAsync(stream, 503, "application/json",
                HttpHelpers.JsonBytes(new { ok = false, error = "Too many clients" }), ct).ConfigureAwait(false);
            client.Dispose();
            return;
        }

        if (!request.Headers.TryGetValue("sec-websocket-key", out var key))
        {
            await HttpHelpers.WriteResponseAsync(stream, 400, "application/json",
                HttpHelpers.JsonBytes(new { ok = false, error = "Missing WebSocket key" }), ct).ConfigureAwait(false);
            client.Dispose();
            return;
        }

        var accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n" +
            "\r\n");
        await stream.WriteAsync(response, ct).ConfigureAwait(false);

        var connection = new WebSocketConnection(client);
        if (!webSocketHub.TryAdd(connection, configuration.MaxClients))
        {
            connection.Dispose();
            return;
        }

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = RunHeartbeatAsync(connection, heartbeatCts.Token);

        try
        {
            await connection.SendTextAsync(
                eventBus.Serialize(Envelope.Create("event.system", new SystemEvent("info", "connected", new
                {
                    clients = webSocketHub.Count,
                    serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }))), ct).ConfigureAwait(false);

            await connection.SendTextAsync(
                eventBus.Serialize(Envelope.Create("event.chat.history", chatMonitor.GetHistory())),
                ct).ConfigureAwait(false);

            await connection.SendTextAsync(
                eventBus.Serialize(Envelope.Create("event.player.snapshot", playerState.GetLatest())),
                ct).ConfigureAwait(false);

            await connection.RunAsync(
                message => HandleMessageAsync(connection, message, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Plugin.Log.Debug(ex, "WebSocket heartbeat finalization failed"); }

            webSocketHub.Remove(connection);
        }
    }

    private async Task RunHeartbeatAsync(WebSocketConnection connection, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false);

            if (DateTime.UtcNow - connection.LastSeenUtc > StaleConnectionTimeout)
            {
                Plugin.Log.Debug("Dropping stale WebSocket connection {Id}", connection.Id);
                webSocketHub.Remove(connection);
                return;
            }

            if (!await TrySendPingAsync(connection, ct).ConfigureAwait(false))
            {
                Plugin.Log.Debug("Dropping WebSocket connection {Id} after heartbeat ping failure", connection.Id);
                webSocketHub.Remove(connection);
                return;
            }
        }
    }

    private static async Task<bool> TrySendPingAsync(WebSocketConnection connection, CancellationToken ct)
    {
        var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pingTask = connection.SendPingAsync([], pingCts.Token);
        var timeoutTask = Task.Delay(PingSendTimeout, ct);

        try
        {
            var completed = await Task.WhenAny(pingTask, timeoutTask).ConfigureAwait(false);
            if (completed != pingTask)
            {
                await pingCts.CancelAsync().ConfigureAwait(false);
                ObserveDetachedPing(pingTask, pingCts);
                return false;
            }

            await pingTask.ConfigureAwait(false);
            pingCts.Dispose();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            pingCts.Dispose();
            throw;
        }
        catch
        {
            pingCts.Dispose();
            return false;
        }
    }

    private static void ObserveDetachedPing(Task pingTask, CancellationTokenSource pingCts)
    {
        _ = pingTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                _ = t.Exception;

            pingCts.Dispose();
        }, TaskScheduler.Default);
    }

    private async Task HandleMessageAsync(
        WebSocketConnection connection, string message, CancellationToken ct)
    {
        Plugin.Log.Debug("WebSocket message received: {Message}", message);
        IncomingEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IncomingEnvelope>(message, PocketJson.Options);
        }
        catch (JsonException ex)
        {
            await connection.SendTextAsync(
                eventBus.Serialize(Envelope.Create("event.command.result", new CommandResult(false, ex.Message))),
                ct).ConfigureAwait(false);
            return;
        }

        if (envelope == null)
            return;

        try
        {
            var result = await commandDispatcher.DispatchAsync(envelope, ct).ConfigureAwait(false);
            await connection.SendTextAsync(
                eventBus.Serialize(Envelope.Create("event.command.result", result)), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "WebSocket command failed: {Type}", envelope.Type);
            await connection.SendTextAsync(
                eventBus.Serialize(Envelope.Create("event.command.result", new CommandResult(false, ex.Message))),
                ct).ConfigureAwait(false);
        }
    }
}
