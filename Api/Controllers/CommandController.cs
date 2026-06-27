using System.Net.Sockets;
using System.Text.Json;
using PocketStation.Api;
using PocketStation.Domain;
using PocketStation.Infrastructure.Network;
using PocketStation.Infrastructure.Serialization;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class CommandController : IHttpController
{
    private readonly CommandDispatcher commandDispatcher;

    public CommandController(CommandDispatcher commandDispatcher)
    {
        this.commandDispatcher = commandDispatcher;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method != "POST" || request.Path != "/api/command")
            return false;

        var envelope = JsonSerializer.Deserialize<IncomingEnvelope>(request.Body, PocketJson.Options);
        if (envelope == null)
        {
            await HttpHelpers.WriteJsonAsync(stream,
                new CommandResult(false, "Invalid command envelope."), ct).ConfigureAwait(false);
            return true;
        }

        var result = await commandDispatcher.DispatchAsync(envelope, ct).ConfigureAwait(false);
        await HttpHelpers.WriteJsonAsync(stream, result, ct).ConfigureAwait(false);
        return true;
    }
}
