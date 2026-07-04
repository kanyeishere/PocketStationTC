using System.Net;
using System.Net.Sockets;
using System.Text;
using PocketStation.Infrastructure.Messaging;

namespace PocketStation.Infrastructure.Network;

public readonly record struct LanWebServerStartResult(
    bool Started,
    int RequestedPort,
    int ActualPort,
    string? ErrorMessage)
{
    public bool PortChanged => Started && RequestedPort != ActualPort;
}

public sealed class LanWebServer : IDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private readonly PocketStation.Host.Configuration configuration;
    private readonly EventBus eventBus;
    private readonly WebSocketHub webSocketHub;
    private readonly Api.WebSocketHandler webSocketHandler;
    private readonly IReadOnlyList<Api.IHttpController> controllers;
    private readonly string staticRoot;

    private CancellationTokenSource? cancellation;
    private TcpListener? listener;
    private Task? acceptLoop;
    private bool disposed;

    public LanWebServer(
        PocketStation.Host.Configuration configuration,
        EventBus eventBus,
        WebSocketHub webSocketHub,
        Api.WebSocketHandler webSocketHandler,
        IEnumerable<Api.IHttpController> controllers,
        string staticRoot)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        this.webSocketHub = webSocketHub;
        this.webSocketHandler = webSocketHandler;
        this.controllers = controllers.ToList();
        this.staticRoot = staticRoot;
    }

    public int ClientCount => webSocketHub.Count;

    public IReadOnlyList<string> AccessUrls =>
        listener == null
            ? Array.Empty<string>()
            : HttpHelpers.GetAccessUrls(configuration.Port, configuration.Token);

    public LanWebServerStartResult Start()
    {
        var requestedPort = configuration.Port;

        if (!configuration.LanEnabled)
            return new LanWebServerStartResult(false, requestedPort, requestedPort, null);

        if (listener != null)
            return new LanWebServerStartResult(true, requestedPort, configuration.Port, null);

        cancellation = new CancellationTokenSource();

        if (!TryStartListener(requestedPort, out var startedListener, out var failure))
        {
            if (failure is SocketException socketException && IsAddressInUse(socketException))
            {
                Plugin.Log.Warning(socketException,
                    "Pocket Station LAN port {Port} is already in use; selecting a free port.", requestedPort);

                if (TryStartListener(0, out startedListener, out var fallbackFailure))
                    return CompleteStart(startedListener!, requestedPort, cancellation.Token);

                failure = fallbackFailure ?? failure;
            }

            CleanupStartAttempt();
            Plugin.Log.Error(failure, "Pocket Station LAN server failed to start on port {Port}", requestedPort);
            return new LanWebServerStartResult(false, requestedPort, requestedPort, failure?.Message);
        }

        return CompleteStart(startedListener!, requestedPort, cancellation.Token);
    }

    private LanWebServerStartResult CompleteStart(TcpListener startedListener, int requestedPort, CancellationToken token)
    {
        listener = startedListener;
        var actualPort = GetPort(startedListener, requestedPort);
        configuration.Port = actualPort;
        eventBus.Published += webSocketHub.BroadcastAsync;
        acceptLoop = Task.Run(() => AcceptLoopAsync(token));

        Plugin.Log.Info("Pocket Station LAN server started on port {Port}", actualPort);
        return new LanWebServerStartResult(true, requestedPort, actualPort, null);
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
            try { await acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        listener = null;
        acceptLoop = null;
        cancellation?.Dispose();
        cancellation = null;
    }

    private void CleanupStartAttempt()
    {
        cancellation?.Dispose();
        cancellation = null;
        listener = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener != null)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch when (ct.IsCancellationRequested) { break; }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private static bool TryStartListener(int port, out TcpListener? startedListener, out Exception? failure)
    {
        TcpListener? candidate = null;
        try
        {
            candidate = new TcpListener(IPAddress.Any, port);
            candidate.Start();
            startedListener = candidate;
            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            try { candidate?.Stop(); }
            catch { }

            startedListener = null;
            failure = ex;
            return false;
        }
    }

    private static int GetPort(TcpListener startedListener, int fallbackPort) =>
        startedListener.LocalEndpoint is IPEndPoint endpoint ? endpoint.Port : fallbackPort;

    private static bool IsAddressInUse(SocketException exception) =>
        exception.SocketErrorCode == SocketError.AddressAlreadyInUse;

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var ownsClient = true;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
            if (request == null) return;

            if (IsWebSocketRequest(request))
            {
                ownsClient = false;
                await webSocketHandler.HandleWebSocketAsync(client, stream, request, ct).ConfigureAwait(false);
                return;
            }

            await HandleHttpAsync(stream, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "LAN request failed");
        }
        finally
        {
            if (ownsClient) client.Dispose();
        }
    }

    private async Task HandleHttpAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
            !HttpHelpers.IsAuthorized(request, configuration.RequireToken ? "1" : null, configuration.Token))
        {
            await HttpHelpers.WriteResponseAsync(stream, 401, "application/json",
                HttpHelpers.JsonBytes(new { ok = false, error = "Unauthorized" }), ct).ConfigureAwait(false);
            return;
        }

        // Dispatch to controllers
        foreach (var controller in controllers)
        {
            if (await controller.TryHandleAsync(stream, request, ct).ConfigureAwait(false))
                return;
        }

        await ServeStaticAsync(stream, request, ct).ConfigureAwait(false);
    }

    // ── Static file serving ──────────────────────────────────

    private async Task ServeStaticAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method != "GET")
        {
            await HttpHelpers.WriteResponseAsync(stream, 405, "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes("Method Not Allowed"), ct).ConfigureAwait(false);
            return;
        }

        var requestPath = request.Path == "/" ? "/index.html" : request.Path;
        var path = requestPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(staticRoot, path));
        var root = Path.GetFullPath(staticRoot);

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            if (!IsSpaFallbackRequest(request))
            {
                await HttpHelpers.WriteResponseAsync(stream, 404, "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Not Found"), ct).ConfigureAwait(false);
                return;
            }

            fullPath = Path.GetFullPath(Path.Combine(staticRoot, "index.html"));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                await HttpHelpers.WriteResponseAsync(stream, 404, "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Not Found"), ct).ConfigureAwait(false);
                return;
            }
        }

        await HttpHelpers.WriteResponseAsync(stream, 200,
            HttpHelpers.GetContentType(fullPath),
            await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false), ct,
            new Dictionary<string, string>
            {
                ["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0",
                ["Pragma"] = "no-cache"
            }).ConfigureAwait(false);
    }

    // ── HTTP parsing ────────────────────────────────────────

    private static bool IsWebSocketRequest(HttpRequest request) =>
        request.Path == "/ws" &&
        request.Headers.TryGetValue("upgrade", out var upgrade) &&
        upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpaFallbackRequest(HttpRequest request) =>
        !request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
        !request.Path.Equals("/ws", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(Path.GetExtension(request.Path));

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        var headerEnd = -1;

        while (memory.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) return null;
            memory.Write(buffer, 0, read);
            headerEnd = FindHeaderEnd(memory.GetBuffer(), (int)memory.Length);
            if (headerEnd >= 0) break;
        }

        if (headerEnd < 0) return null;

        var raw = memory.ToArray();
        var headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(':');
            if (idx <= 0) continue;
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

        headers.TryGetValue("content-length", out var cl);
        var contentLength = int.TryParse(cl, out var parsed) ? parsed : 0;
        if (contentLength > MaxBodyBytes)
            throw new InvalidOperationException("HTTP request body is too large.");

        var body = new byte[contentLength];
        var bodyStart = headerEnd + 4;
        var prefixLength = Math.Min(contentLength, raw.Length - bodyStart);
        if (prefixLength > 0) Array.Copy(raw, bodyStart, body, 0, prefixLength);

        var offset = prefixLength;
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), ct).ConfigureAwait(false);
            if (read == 0) break;
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
            if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' &&
                buffer[i - 1] == '\r' && buffer[i] == '\n')
                return i - 3;
        return -1;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
                result[Uri.UnescapeDataString(part)] = string.Empty;
            else
                result[Uri.UnescapeDataString(part[..idx])] =
                    Uri.UnescapeDataString(part[(idx + 1)..].Replace("+", " "));
        }

        return result;
    }
}
