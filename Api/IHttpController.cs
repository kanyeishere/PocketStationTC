using System.Net.Sockets;
using PocketStation.Infrastructure.Network;

namespace PocketStation.Api;

/// <summary>
/// HTTP controller interface. Each controller handles a specific route group.
/// </summary>
public interface IHttpController
{
    Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct);
}
