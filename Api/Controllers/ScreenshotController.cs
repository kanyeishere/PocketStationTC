using System.Net.Sockets;
using PocketStation.Domain;
using PocketStation.Infrastructure.Network;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class ScreenshotController : IHttpController
{
    private readonly ScreenshotModule screenshotModule;

    public ScreenshotController(ScreenshotModule screenshotModule)
    {
        this.screenshotModule = screenshotModule;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "GET" && request.Path.StartsWith("/api/screen/latest.jpg", StringComparison.OrdinalIgnoreCase))
        {
            var latestPath = screenshotModule.LatestPath;
            if (latestPath == null || !File.Exists(latestPath))
            {
                await HttpHelpers.WriteResponseAsync(stream, 404, "application/json",
                    HttpHelpers.JsonBytes(new { ok = false, error = "No screenshot captured yet" }), ct).ConfigureAwait(false);
                return true;
            }

            await HttpHelpers.WriteResponseAsync(stream, 200, "image/jpeg",
                await File.ReadAllBytesAsync(latestPath, ct).ConfigureAwait(false), ct,
                new Dictionary<string, string> { ["Cache-Control"] = "no-store" }).ConfigureAwait(false);
            return true;
        }

        if ((request.Method == "POST" || request.Method == "GET") && request.Path == "/api/screen/capture")
        {
            try
            {
                Plugin.Log.Info("HTTP screenshot capture requested from LAN client");
                var screenshot = await screenshotModule.CaptureAsync(ct).ConfigureAwait(false);
                await HttpHelpers.WriteJsonAsync(stream,
                    new CommandResult(true, "screenshot captured", screenshot), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "HTTP screenshot capture failed");
                await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                    HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }
}
