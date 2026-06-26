using System.Text.Json;
using PocketStation.Game;
using PocketStation.Modules;
using PocketStation.Protocol;

namespace PocketStation.Core;

public sealed class CommandDispatcher
{
    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly GameFacade game;
    private readonly ScreenshotModule screenshotModule;

    public Func<int, Task>? OnStartStream { get; set; }
    public Func<Task>? OnStopStream { get; set; }

    public CommandDispatcher(
        Configuration configuration,
        EventBus eventBus,
        GameFacade game,
        ScreenshotModule screenshotModule)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        this.game = game;
        this.screenshotModule = screenshotModule;
    }

    public async Task<CommandResult> DispatchAsync(IncomingEnvelope envelope, CancellationToken cancellationToken)
    {
        return envelope.Type switch
        {
            "cmd.sendChat" => await DispatchSendChatAsync(envelope.Payload).ConfigureAwait(false),
            "cmd.requestScreenshot" => await DispatchScreenshotAsync(envelope.Payload, cancellationToken).ConfigureAwait(false),
            "cmd.startStream" => await DispatchStartStreamAsync(envelope.Payload).ConfigureAwait(false),
            "cmd.stopStream" => await DispatchStopStreamAsync().ConfigureAwait(false),
            "cmd.ping" => new CommandResult(true, "pong", new { serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }),
            _ => new CommandResult(false, $"Unknown command type: {envelope.Type}")
        };
    }

    private async Task<CommandResult> DispatchSendChatAsync(JsonElement payload)
    {
        var command = payload.Deserialize<SendChatCommand>(Plugin.JsonOptions);
        return await SendChatAsync(command?.Content ?? string.Empty).ConfigureAwait(false);
    }

    public async Task<CommandResult> SendChatAsync(string content)
    {
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return new CommandResult(false, "Message is empty.");

        var normalized = NormalizeChatCommand(content);
        Plugin.Log.Info("Remote chat send requested: {Command}", normalized);
        var ok = await game.SendChatOrCommandAsync(normalized).ConfigureAwait(false);
        var result = new CommandResult(ok, ok ? "sent" : "Game rejected the command.");
        eventBus.Publish("event.command.result", result);
        return result;
    }

    private async Task<CommandResult> DispatchScreenshotAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        try
        {
            var command = payload.ValueKind == JsonValueKind.Object
                ? payload.Deserialize<RequestScreenshotCommand>(Plugin.JsonOptions)
                : new RequestScreenshotCommand();

            var screenshot = await screenshotModule.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var result = new CommandResult(true, "screenshot captured", screenshot);

            if (command?.Broadcast == true)
                eventBus.Publish("event.command.result", result);

            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Screenshot command failed");
            var result = new CommandResult(false, $"screenshot failed: {ex.Message}");
            eventBus.Publish("event.command.result", result);
            eventBus.Publish("event.system", new SystemEvent("error", result.Message));
            return result;
        }
    }

    private static string NormalizeChatCommand(string content)
    {
        return content.Trim();
    }

    private async Task<CommandResult> DispatchStartStreamAsync(JsonElement payload)
    {
        try
        {
            var command = payload.ValueKind == JsonValueKind.Object
                ? payload.Deserialize<StartStreamCommand>(Plugin.JsonOptions)
                : new StartStreamCommand();

            var fps = command?.Fps ?? configuration.StreamFps;
            fps = Math.Clamp(fps, 1, 120);

            if (OnStartStream == null)
                return new CommandResult(false, "Streaming is not available.");

            await OnStartStream(fps).ConfigureAwait(false);
            configuration.StreamFps = fps;

            return new CommandResult(true, "stream started", new { fps });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "StartStream command failed");
            return new CommandResult(false, $"startStream failed: {ex.Message}");
        }
    }

    private async Task<CommandResult> DispatchStopStreamAsync()
    {
        try
        {
            if (OnStopStream == null)
                return new CommandResult(false, "Streaming is not available.");

            await OnStopStream().ConfigureAwait(false);
            return new CommandResult(true, "stream stopped");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "StopStream command failed");
            return new CommandResult(false, $"stopStream failed: {ex.Message}");
        }
    }
}
