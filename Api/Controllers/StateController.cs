using System.Net.Sockets;
using PocketStation.Infrastructure.Network;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class StateController : IHttpController
{
    private readonly PlayerStateModule playerState;

    public StateController(PlayerStateModule playerState)
    {
        this.playerState = playerState;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method != "GET" || request.Path != "/api/state")
            return false;

        await HttpHelpers.WriteJsonAsync(stream, playerState.GetLatest(), ct).ConfigureAwait(false);
        return true;
    }
}
