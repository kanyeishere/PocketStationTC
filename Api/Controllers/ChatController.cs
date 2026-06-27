using System.Net.Sockets;
using System.Text.Json;
using PocketStation.Api;
using PocketStation.Domain;
using PocketStation.Host;
using PocketStation.Infrastructure.Network;
using PocketStation.Infrastructure.Serialization;
using PocketStation.Services;

namespace PocketStation.Api.Controllers;

public sealed class ChatController : IHttpController
{
    private readonly Configuration configuration;
    private readonly CommandDispatcher commandDispatcher;
    private readonly ChatMonitorModule chatMonitor;
    private readonly ChatTypeCatalogService chatTypeCatalog;
    private readonly Action saveConfiguration;

    public ChatController(
        Configuration configuration,
        CommandDispatcher commandDispatcher,
        ChatMonitorModule chatMonitor,
        ChatTypeCatalogService chatTypeCatalog,
        Action saveConfiguration)
    {
        this.configuration = configuration;
        this.commandDispatcher = commandDispatcher;
        this.chatMonitor = chatMonitor;
        this.chatTypeCatalog = chatTypeCatalog;
        this.saveConfiguration = saveConfiguration;
    }

    public async Task<bool> TryHandleAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        return request.Path switch
        {
            "/api/chat/send" when request.Method is "POST" or "GET" => await HandleSendAsync(stream, request, ct).ConfigureAwait(false),
            "/api/chat/history" when request.Method == "GET" => await HandleHistoryAsync(stream, ct).ConfigureAwait(false),
            "/api/chat/modes" when request.Method == "GET" => await HandleModesGetAsync(stream, ct).ConfigureAwait(false),
            "/api/chat/modes" when request.Method == "POST" => await HandleModesPostAsync(stream, request, ct).ConfigureAwait(false),
            _ => false
        };
    }

    private async Task<bool> HandleSendAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        try
        {
            var content = string.Empty;
            if (request.Method == "GET")
            {
                request.Query.TryGetValue("content", out content);
                if (string.IsNullOrWhiteSpace(content))
                    request.Query.TryGetValue("message", out content);
            }
            else if (request.Body.Length > 0)
            {
                var command = JsonSerializer.Deserialize<SendChatCommand>(request.Body, PocketJson.Options);
                content = command?.Content ?? string.Empty;
            }

            Plugin.Log.Info("HTTP chat send requested from LAN client");
            var result = await commandDispatcher.SendChatAsync(content ?? string.Empty).ConfigureAwait(false);
            await HttpHelpers.WriteJsonAsync(stream, result, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "HTTP chat send failed");
            await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> HandleHistoryAsync(NetworkStream stream, CancellationToken ct)
    {
        await HttpHelpers.WriteJsonAsync(stream, chatMonitor.GetHistory(), ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandleModesGetAsync(NetworkStream stream, CancellationToken ct)
    {
        await HttpHelpers.WriteJsonAsync(stream, CreateSettings(), ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandleModesPostAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<ChatFilterSettings>(request.Body, PocketJson.Options);
            if (settings == null)
            {
                await HttpHelpers.WriteResponseAsync(stream, 400, "application/json",
                    HttpHelpers.JsonBytes(new CommandResult(false, "Invalid chat filter settings.")), ct).ConfigureAwait(false);
                return true;
            }

            ChatFilterDefaults.ApplySettings(configuration, settings);
            saveConfiguration();

            await HttpHelpers.WriteJsonAsync(stream, CreateSettings(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to save chat filter modes");
            await HttpHelpers.WriteResponseAsync(stream, 500, "application/json",
                HttpHelpers.JsonBytes(new CommandResult(false, ex.Message)), ct).ConfigureAwait(false);
        }

        return true;
    }

    private ChatFilterSettings CreateSettings() =>
        ChatFilterDefaults.CreateSettings(configuration, chatTypeCatalog.GetOptions());
}
