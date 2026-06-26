using System.Text.Json;
using PocketStation.Host;
using PocketStation.Infrastructure.Game;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Services;
using PocketStation.Domain;

namespace PocketStation.Api;

public sealed class CommandDispatcher
{
    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly GameFacade game;
    private readonly ScreenshotModule screenshotModule;

    public Func<int, Task>? OnStartStream { get; set; }
    public Func<Task>? OnStopStream { get; set; }
    public Func<string, bool, Task<string?>>? OnTogglePlugin { get; set; }

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
            "cmd.enablePlugin" => await DispatchTogglePluginAsync(envelope.Payload, true).ConfigureAwait(false),
            "cmd.disablePlugin" => await DispatchTogglePluginAsync(envelope.Payload, false).ConfigureAwait(false),
            "cmd.ping" => new CommandResult(true, "pong", new { serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }),
            _ => new CommandResult(false, $"未知指令类型：{envelope.Type}")
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
            return new CommandResult(false, "消息不能为空。");

        var normalized = NormalizeChatCommand(content);
        Plugin.Log.Info("Remote chat send requested: {Command}", normalized);
        var ok = await game.SendChatOrCommandAsync(normalized).ConfigureAwait(false);
        var result = new CommandResult(ok, ok ? "已发送" : "游戏拒绝了该指令。");
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
            var result = new CommandResult(true, "截图已捕获", screenshot);

            if (command?.Broadcast == true)
                eventBus.Publish("event.command.result", result);

            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Screenshot command failed");
            var result = new CommandResult(false, $"截图失败：{ex.Message}");
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
                return new CommandResult(false, "串流不可用。");

            await OnStartStream(fps).ConfigureAwait(false);
            configuration.StreamFps = fps;

            return new CommandResult(true, "串流已启动", new { fps });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "StartStream command failed");
            return new CommandResult(false, $"启动串流失败：{ex.Message}");
        }
    }

    private async Task<CommandResult> DispatchStopStreamAsync()
    {
        try
        {
            if (OnStopStream == null)
                return new CommandResult(false, "串流不可用。");

            await OnStopStream().ConfigureAwait(false);
            return new CommandResult(true, "串流已停止");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "StopStream command failed");
            return new CommandResult(false, $"停止串流失败：{ex.Message}");
        }
    }

    private async Task<CommandResult> DispatchTogglePluginAsync(JsonElement payload, bool enable)
    {
        var command = payload.Deserialize<TogglePluginCommand>(Plugin.JsonOptions);
        var internalName = command?.InternalName?.Trim();
        if (string.IsNullOrWhiteSpace(internalName))
            return new CommandResult(false, "需要提供插件内部名称。");

        // Block self-disable
        if (!enable && internalName.Equals("PocketStation", StringComparison.OrdinalIgnoreCase))
            return new CommandResult(false, "不能从网页控制台禁用 Pocket Station。");

        if (OnTogglePlugin == null)
            return new CommandResult(false, "插件管理不可用。");

        var error = await OnTogglePlugin(internalName, enable).ConfigureAwait(false);
        if (error != null)
            return new CommandResult(false, error);

        var action = enable ? "已启用" : "已禁用";
        var message = $"[Pocket Station] 插件 '{internalName}' 已被远程{action}。";
        Plugin.Log.Info(message);
        game.PrintChat(message);

        return new CommandResult(true, $"plugin {action}", new { internalName });
    }
}
