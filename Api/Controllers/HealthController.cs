using System.Net.Sockets;
using PocketStation.Host;
using PocketStation.Infrastructure.Network;

namespace PocketStation.Api.Controllers;

public sealed class HealthController : IHttpController
{
    private readonly Configuration configuration;
    private readonly WebSocketHub webSocketHub;

    public HealthController(Configuration configuration, WebSocketHub webSocketHub)
    {
        this.configuration = configuration;
        this.webSocketHub = webSocketHub;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method != "GET" || request.Path != "/api/health")
            return false;

        await HttpHelpers.WriteJsonAsync(stream, new
        {
            ok = true,
            lanEnabled = configuration.LanEnabled,
            port = configuration.Port,
            clients = webSocketHub.Count,
            urls = HttpHelpers.GetAccessUrls(configuration.Port, configuration.Token)
        }, ct).ConfigureAwait(false);
        return true;
    }
}
