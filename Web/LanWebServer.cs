using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmenTools;
using PocketStation.Core;
using PocketStation.Modules;
using PocketStation.Protocol;

namespace PocketStation.Web;

public sealed class LanWebServer : IDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;

    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly WebSocketHub webSocketHub;
    private readonly CommandDispatcher commandDispatcher;
    private readonly ChatMonitorModule chatMonitor;
    private readonly PlayerStateModule playerState;
    private readonly ScreenshotModule screenshotModule;
    private readonly string staticRoot;
    private readonly Action saveConfiguration;

    private CancellationTokenSource? cancellation;
    private TcpListener? listener;
    private Task? acceptLoop;
    private bool disposed;

    public LanWebServer(
        Configuration configuration,
        EventBus eventBus,
        CommandDispatcher commandDispatcher,
        ChatMonitorModule chatMonitor,
        PlayerStateModule playerState,
        ScreenshotModule screenshotModule,
        string staticRoot,
        Action saveConfiguration)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        this.commandDispatcher = commandDispatcher;
        this.chatMonitor = chatMonitor;
        this.playerState = playerState;
        this.screenshotModule = screenshotModule;
        this.staticRoot = staticRoot;
        this.saveConfiguration = saveConfiguration;
        webSocketHub = new WebSocketHub(eventBus);
    }

    public int ClientCount => webSocketHub.Count;

    public IReadOnlyList<string> AccessUrls => GetAccessUrls();

    public void Start()
    {
        if (!configuration.LanEnabled || listener != null)
            return;

        cancellation = new CancellationTokenSource();
        listener = new TcpListener(IPAddress.Any, configuration.Port);
        listener.Start();
        eventBus.Published += webSocketHub.BroadcastAsync;
        acceptLoop = Task.Run(() => AcceptLoopAsync(cancellation.Token));

        Plugin.Log.Info("Pocket Station LAN server started on port {Port}", configuration.Port);
    }

    public async Task StopAsync()
    {
        if (listener == null)
            return;

        eventBus.Published -= webSocketHub.BroadcastAsync;
        cancellation?.Cancel();
        listener.Stop();

        if (acceptLoop != null)
        {
            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        listener = null;
        acceptLoop = null;
        cancellation?.Dispose();
        cancellation = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener != null)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var ownsClient = true;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            if (request == null)
                return;

            if (IsWebSocketRequest(request))
            {
                ownsClient = false;
                await HandleWebSocketAsync(client, stream, request, cancellationToken).ConfigureAwait(false);
                return;
            }

            await HandleHttpAsync(stream, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "LAN request failed");
        }
        finally
        {
            if (ownsClient)
                client.Dispose();
        }
    }

    private async Task HandleWebSocketAsync(
        TcpClient client,
        NetworkStream stream,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(request))
        {
            await WriteResponseAsync(stream, 401, "application/json", JsonBytes(new { ok = false, error = "Unauthorized" }), cancellationToken).ConfigureAwait(false);
            client.Dispose();
            return;
        }

        if (webSocketHub.Count >= configuration.MaxClients)
        {
            await WriteResponseAsync(stream, 503, "application/json", JsonBytes(new { ok = false, error = "Too many clients" }), cancellationToken).ConfigureAwait(false);
            client.Dispose();
            return;
        }

        if (!request.Headers.TryGetValue("sec-websocket-key", out var key))
        {
            await WriteResponseAsync(stream, 400, "application/json", JsonBytes(new { ok = false, error = "Missing WebSocket key" }), cancellationToken).ConfigureAwait(false);
            client.Dispose();
            return;
        }

        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n" +
            "\r\n");
        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);

        var connection = new WebSocketConnection(client);
        if (!webSocketHub.TryAdd(connection, configuration.MaxClients))
        {
            connection.Dispose();
            return;
        }

        try
        {
            await connection.SendTextAsync(eventBus.Serialize(Envelope.Create("event.system", new SystemEvent("info", "connected", new
            {
                clients = webSocketHub.Count,
                serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }))), cancellationToken).ConfigureAwait(false);

            await connection.SendTextAsync(eventBus.Serialize(Envelope.Create("event.chat.history", chatMonitor.GetHistory())), cancellationToken).ConfigureAwait(false);
            await connection.SendTextAsync(eventBus.Serialize(Envelope.Create("event.player.snapshot", playerState.GetLatest())), cancellationToken).ConfigureAwait(false);

            await connection.RunAsync(message => HandleWebSocketMessageAsync(connection, message, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            webSocketHub.Remove(connection);
        }
    }

    private async Task HandleWebSocketMessageAsync(WebSocketConnection connection, string message, CancellationToken cancellationToken)
    {
        Plugin.Log.Debug("WebSocket message received: {Message}", message);
        IncomingEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IncomingEnvelope>(message, Plugin.JsonOptions);
        }
        catch (JsonException ex)
        {
            await connection.SendTextAsync(eventBus.Serialize(Envelope.Create("event.command.result", new CommandResult(false, ex.Message))), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (envelope == null)
            return;

        try
        {
            var result = await commandDispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            await connection.SendTextAsync(eventBus.Serialize(Envelope.Create("event.command.result", result)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "WebSocket command failed: {Type}", envelope.Type);
            await connection.SendTextAsync(eventBus.Serialize(Envelope.Create("event.command.result", new CommandResult(false, ex.Message))), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleHttpAsync(NetworkStream stream, HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !IsAuthorized(request))
        {
            await WriteResponseAsync(stream, 401, "application/json", JsonBytes(new { ok = false, error = "Unauthorized" }), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method == "GET" && request.Path == "/api/health")
        {
            await WriteJsonAsync(stream, new
            {
                ok = true,
                lanEnabled = configuration.LanEnabled,
                port = configuration.Port,
                clients = webSocketHub.Count,
                urls = AccessUrls
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method == "GET" && request.Path == "/api/debug/beep")
        {
            Plugin.Log.Info("Pocket Station debug beep received from LAN client");
            DService.Instance().Chat.Print("[Pocket Station] debug beep received from LAN client");
            await WriteJsonAsync(stream, new
            {
                ok = true,
                message = "beep received",
                serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if ((request.Method == "POST" || request.Method == "GET") && request.Path == "/api/chat/send")
        {
            try
            {
                var content = string.Empty;
                if (request.Method == "GET")
                {
                    request.Query.TryGetValue("content", out content);
                    if (string.IsNullOrWhiteSpace(content))
                        request.Query.TryGetValue("message", out content);
                }
                else if (request.Body.Length > 0)
                {
                    var command = JsonSerializer.Deserialize<SendChatCommand>(request.Body, Plugin.JsonOptions);
                    content = command?.Content ?? string.Empty;
                }

                Plugin.Log.Info("HTTP chat send requested from LAN client");
                var result = await commandDispatcher.SendChatAsync(content ?? string.Empty).ConfigureAwait(false);
                await WriteJsonAsync(stream, result, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "HTTP chat send failed");
                await WriteResponseAsync(stream, 500, "application/json", JsonBytes(new CommandResult(false, ex.Message)), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (request.Method == "GET" && request.Path == "/api/chat/history")
        {
            await WriteJsonAsync(stream, chatMonitor.GetHistory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method == "GET" && request.Path == "/api/chat/modes")
        {
            await WriteJsonAsync(stream, ChatFilterDefaults.CreateSettings(configuration), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method == "POST" && request.Path == "/api/chat/modes")
        {
            try
            {
                var settings = JsonSerializer.Deserialize<ChatFilterSettings>(request.Body, Plugin.JsonOptions);
                if (settings == null)
                {
                    await WriteResponseAsync(stream, 400, "application/json", JsonBytes(new CommandResult(false, "Invalid chat filter settings.")), cancellationToken).ConfigureAwait(false);
                    return;
                }

                ChatFilterDefaults.ApplySettings(configuration, settings);
                saveConfiguration();

                await WriteJsonAsync(stream, ChatFilterDefaults.CreateSettings(configuration), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to save chat filter modes");
                await WriteResponseAsync(stream, 500, "application/json", JsonBytes(new CommandResult(false, ex.Message)), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (request.Method == "GET" && request.Path == "/api/state")
        {
            await WriteJsonAsync(stream, playerState.GetLatest(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method == "GET" && request.Path.StartsWith("/api/screen/latest.jpg", StringComparison.OrdinalIgnoreCase))
        {
            var latestPath = screenshotModule.LatestPath;
            if (latestPath == null || !File.Exists(latestPath))
            {
                await WriteResponseAsync(stream, 404, "application/json", JsonBytes(new { ok = false, error = "No screenshot captured yet" }), cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(stream, 200, "image/jpeg", await File.ReadAllBytesAsync(latestPath, cancellationToken).ConfigureAwait(false), cancellationToken, new Dictionary<string, string>
            {
                ["Cache-Control"] = "no-store"
            }).ConfigureAwait(false);
            return;
        }

        if ((request.Method == "POST" || request.Method == "GET") && request.Path == "/api/screen/capture")
        {
            try
            {
                Plugin.Log.Info("HTTP screenshot capture requested from LAN client");
                var screenshot = await screenshotModule.CaptureAsync(cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(stream, new CommandResult(true, "screenshot captured", screenshot), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "HTTP screenshot capture failed");
                await WriteResponseAsync(stream, 500, "application/json", JsonBytes(new CommandResult(false, ex.Message)), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (request.Method == "POST" && request.Path == "/api/command")
        {
            var envelope = JsonSerializer.Deserialize<IncomingEnvelope>(request.Body, Plugin.JsonOptions);
            if (envelope == null)
            {
                await WriteJsonAsync(stream, new CommandResult(false, "Invalid command envelope."), cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await commandDispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(stream, result, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ServeStaticAsync(stream, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task ServeStaticAsync(NetworkStream stream, HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method != "GET")
        {
            await WriteResponseAsync(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method Not Allowed"), cancellationToken).ConfigureAwait(false);
            return;
        }

        var path = request.Path == "/" ? "/index.html" : request.Path;
        path = path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(staticRoot, path));
        var root = Path.GetFullPath(staticRoot);

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"), cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(stream, 200, GetContentType(fullPath), await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false), cancellationToken, new Dictionary<string, string>
        {
            ["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0",
            ["Pragma"] = "no-cache"
        }).ConfigureAwait(false);
    }

    private bool IsAuthorized(HttpRequest request)
    {
        if (!configuration.RequireToken)
            return true;

        if (request.Query.TryGetValue("token", out var queryToken) && ConstantTimeEquals(queryToken, configuration.Token))
            return true;

        if (request.Headers.TryGetValue("x-pocket-token", out var headerToken) && ConstantTimeEquals(headerToken, configuration.Token))
            return true;

        if (request.Headers.TryGetValue("authorization", out var authorization) &&
            authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            ConstantTimeEquals(authorization[7..].Trim(), configuration.Token))
            return true;

        return false;
    }

    private IReadOnlyList<string> GetAccessUrls()
    {
        var urls = new List<string>();
        foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                urls.Add($"http://{address}:{configuration.Port}/?token={Uri.EscapeDataString(configuration.Token)}");
        }

        if (urls.Count == 0)
            urls.Add($"http://127.0.0.1:{configuration.Port}/?token={Uri.EscapeDataString(configuration.Token)}");

        return urls;
    }

    private static bool IsWebSocketRequest(HttpRequest request)
    {
        return request.Path == "/ws" &&
               request.Headers.TryGetValue("upgrade", out var upgrade) &&
               upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        var headerEnd = -1;

        while (memory.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            memory.Write(buffer, 0, read);
            headerEnd = FindHeaderEnd(memory.GetBuffer(), (int)memory.Length);
            if (headerEnd >= 0)
                break;
        }

        if (headerEnd < 0)
            return null;

        var raw = memory.ToArray();
        var headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(':');
            if (idx <= 0)
                continue;

            headers[lines[i][..idx].Trim().ToLowerInvariant()] = lines[i][(idx + 1)..].Trim();
        }

        var target = requestLine[1];
        var path = target;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queryIndex = target.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = target[..queryIndex];
            query = ParseQuery(target[(queryIndex + 1)..]);
        }

        headers.TryGetValue("content-length", out var contentLengthValue);
        var contentLength = int.TryParse(contentLengthValue, out var parsedLength) ? parsedLength : 0;
        var body = new byte[contentLength];
        var bodyStart = headerEnd + 4;
        var prefixLength = Math.Min(contentLength, raw.Length - bodyStart);
        if (prefixLength > 0)
            Array.Copy(raw, bodyStart, body, 0, prefixLength);

        var offset = prefixLength;
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            offset += read;
        }

        return new HttpRequest(
            requestLine[0].ToUpperInvariant(),
            Uri.UnescapeDataString(path),
            query,
            headers,
            body);
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (var i = 3; i < length; i++)
        {
            if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                return i - 3;
        }

        return -1;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(part)] = string.Empty;
                continue;
            }

            result[Uri.UnescapeDataString(part[..idx])] = Uri.UnescapeDataString(part[(idx + 1)..].Replace("+", " "));
        }

        return result;
    }

    private static async Task WriteJsonAsync(NetworkStream stream, object value, CancellationToken cancellationToken)
    {
        await WriteResponseAsync(stream, 200, "application/json; charset=utf-8", JsonBytes(value), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            503 => "Service Unavailable",
            _ => "OK"
        };

        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        builder.Append("Connection: close\r\n");
        builder.Append("Access-Control-Allow-Origin: *\r\n");
        builder.Append("Access-Control-Allow-Headers: Content-Type, X-Pocket-Token, Authorization\r\n");
        if (extraHeaders != null)
        {
            foreach (var (key, value) in extraHeaders)
                builder.Append(key).Append(": ").Append(value).Append("\r\n");
        }

        builder.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] JsonBytes(object value) => JsonSerializer.SerializeToUtf8Bytes(value, Plugin.JsonOptions);

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private static bool ConstantTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record HttpRequest(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Query,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);
}
