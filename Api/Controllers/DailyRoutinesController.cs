using System.Net.Sockets;
using PocketStation.Infrastructure.Network;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class DailyRoutinesController : IHttpController
{
    private readonly DailyRoutinesService dailyRoutines;

    public DailyRoutinesController(DailyRoutinesService dailyRoutines)
    {
        this.dailyRoutines = dailyRoutines;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "GET" && request.Path == "/api/dailyroutines")
        {
            var snapshot = dailyRoutines.CaptureSnapshot();
            await HttpHelpers.WriteJsonAsync(stream, snapshot, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }
}
