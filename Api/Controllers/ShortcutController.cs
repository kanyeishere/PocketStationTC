using System.Net.Sockets;
using System.Text.Json;
using PocketStation.Domain;
using PocketStation.Host;
using PocketStation.Infrastructure.Network;
using PocketStation.Infrastructure.Serialization;

namespace PocketStation.Api.Controllers;

public sealed class ShortcutController : IHttpController
{
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;

    public ShortcutController(Configuration configuration, Action saveConfiguration)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "GET" && request.Path == "/api/shortcuts")
        {
            await HttpHelpers.WriteJsonAsync(stream, configuration.CommandShortcuts, ct).ConfigureAwait(false);
            return true;
        }

        if (request.Method == "POST" && request.Path == "/api/shortcuts")
        {
            try
            {
                var shortcuts = JsonSerializer.Deserialize<List<CommandShortcut>>(request.Body, PocketJson.Options);
                if (shortcuts == null)
                {
                    await HttpHelpers.WriteResponseAsync(stream, 400, "application/json",
                        HttpHelpers.JsonBytes(new CommandResult(false, "Invalid shortcuts.")), ct).ConfigureAwait(false);
                    return true;
                }

                configuration.CommandShortcuts = shortcuts;
                saveConfiguration();
                await HttpHelpers.WriteJsonAsync(stream,
                    new CommandResult(true, "shortcuts saved", shortcuts), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to save shortcuts");
                await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                    HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }
}
