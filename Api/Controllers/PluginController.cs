using System.Net.Sockets;
using PocketStation.Api;
using PocketStation.Domain;
using PocketStation.Infrastructure.Network;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class PluginController : IHttpController
{
    private readonly CommandDispatcher commandDispatcher;
    private readonly DalamudPluginService pluginService;

    public PluginController(
        CommandDispatcher commandDispatcher,
        DalamudPluginService pluginService)
    {
        this.commandDispatcher = commandDispatcher;
        this.pluginService = pluginService;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "GET" && request.Path == "/api/plugins")
        {
            await HttpHelpers.WriteJsonAsync(stream, pluginService.GetInstalledPlugins(), ct).ConfigureAwait(false);
            return true;
        }

        if (request.Method == "POST" && TryMatchPluginAction(request.Path, out var name, out var enable))
        {
            try
            {
                var result = await commandDispatcher.DispatchAsync(
                    IncomingEnvelopeFactory.FromPayload(enable ? "cmd.enablePlugin" : "cmd.disablePlugin",
                        new TogglePluginCommand(name)), ct).ConfigureAwait(false);
                await HttpHelpers.WriteJsonAsync(stream, result, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Plugin toggle failed: {Name}", name);
                await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                    HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }

    private static bool TryMatchPluginAction(string path, out string internalName, out bool enable)
    {
        internalName = string.Empty;
        enable = false;

        const string prefix = "/api/plugins/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = path[prefix.Length..];
        if (suffix.EndsWith("/enable", StringComparison.OrdinalIgnoreCase))
        {
            internalName = Uri.UnescapeDataString(suffix[..^"/enable".Length]);
            enable = true;
            return internalName.Length > 0;
        }

        if (suffix.EndsWith("/disable", StringComparison.OrdinalIgnoreCase))
        {
            internalName = Uri.UnescapeDataString(suffix[..^"/disable".Length]);
            return internalName.Length > 0;
        }

        return false;
    }
}
