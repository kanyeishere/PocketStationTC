using System.Net.Sockets;
using System.Text.Json;
using PocketStation.Domain;
using PocketStation.Host;
using PocketStation.Infrastructure.Network;
using PocketStation.Infrastructure.Serialization;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class StreamController : IHttpController
{
    private readonly Configuration configuration;
    private readonly WebSocketHub webSocketHub;
    private readonly ScreenshotModule screenshotModule;
    private readonly Action saveConfiguration;

    public StreamController(
        Configuration configuration,
        WebSocketHub webSocketHub,
        ScreenshotModule screenshotModule,
        Action saveConfiguration)
    {
        this.configuration = configuration;
        this.webSocketHub = webSocketHub;
        this.screenshotModule = screenshotModule;
        this.saveConfiguration = saveConfiguration;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "GET" && request.Path == "/api/stream/config")
        {
            await HttpHelpers.WriteJsonAsync(stream, new
            {
                fps = configuration.StreamFps,
                running = screenshotModule.IsStreaming
            }, ct).ConfigureAwait(false);
            return true;
        }

        if (request.Method == "POST" && request.Path == "/api/stream/start")
        {
            try
            {
                var command = request.Body.Length > 0
                    ? JsonSerializer.Deserialize<StartStreamCommand>(request.Body, PocketJson.Options)
                    : new StartStreamCommand();

                var fps = Math.Clamp(command?.Fps ?? configuration.StreamFps, 1, 120);
                await screenshotModule.StartStreamingAsync(fps,
                    (frame, token) => webSocketHub.BroadcastBinaryAsync(frame, token)).ConfigureAwait(false);
                configuration.StreamFps = fps;
                saveConfiguration();

                await HttpHelpers.WriteJsonAsync(stream,
                    new CommandResult(true, "stream started", new { fps }), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "HTTP stream start failed");
                await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                    HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
            }

            return true;
        }

        if (request.Method == "POST" && request.Path == "/api/stream/stop")
        {
            try
            {
                await screenshotModule.StopStreamingAsync().ConfigureAwait(false);
                await HttpHelpers.WriteJsonAsync(stream,
                    new CommandResult(true, "stream stopped"), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "HTTP stream stop failed");
                await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                    HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }
}
