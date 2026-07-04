using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.Dalamud.Helpers;
using PocketStation.Api;
using PocketStation.Api.Controllers;
using PocketStation.Helpers;
using PocketStation.Infrastructure.Game;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Infrastructure.Network;
using PocketStation.Infrastructure.Telemetry;
using PocketStation.Services;

namespace PocketStation.Host;

internal sealed class PocketStationRuntime : IDisposable
{
    private const string CommandName = "/pocketstation";
    private const string ShortCommandName = "/ps";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly Configuration configuration;
    private readonly GameFacade game;
    private readonly PocketModuleHost moduleHost;
    private readonly ScreenshotModule screenshotModule;
    private readonly LanWebServer webServer;
    private readonly PocketStationConfigWindow configWindow;
    private readonly PocketStationFloatingWindow floatingWindow;

    private bool disposed;

    public PocketStationRuntime(
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        ICommandManager commandManager,
        IClientState clientState,
        IDataManager dataManager,
        IObjectTable objectTable,
        IPartyList partyList,
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        DService.Init(pluginInterface);

        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();
        SaveConfiguration();

        var eventBus = new EventBus();
        game = new GameFacade(chatGui, commandManager, clientState, objectTable, partyList, framework);
        game.Initialize();

        var configDirectory = pluginInterface.GetPluginConfigDirectory();
        screenshotModule = new ScreenshotModule(configuration, eventBus, configDirectory);
        var dailyRoutines = new DailyRoutinesService(configDirectory);
        var pluginService = new DalamudPluginService(pluginInterface);
        var chatTypeCatalog = new ChatTypeCatalogService(dataManager);
        var chatMonitor = new ChatMonitorModule(configuration, eventBus, game);
        var playerState = new PlayerStateModule(configuration, eventBus, game, framework);
        var commandDispatcher = new CommandDispatcher(configuration, eventBus, game, screenshotModule)
        {
            OnTogglePlugin = DalamudReflectorEx.SetPluginStateAsync
        };

        moduleHost = new PocketModuleHost();
        moduleHost.Add(chatMonitor);
        moduleHost.Add(playerState);
        moduleHost.Add(screenshotModule);
        moduleHost.Initialize();

        var staticRoot = Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName ?? AppContext.BaseDirectory, "wwwroot");
        var webSocketHub = new WebSocketHub(eventBus);
        var webSocketHandler = new WebSocketHandler(
            configuration, eventBus, webSocketHub, commandDispatcher, chatMonitor, playerState);

        var controllers = new IHttpController[]
        {
            new HealthController(configuration, webSocketHub),
            new ChatController(configuration, commandDispatcher, chatMonitor, chatTypeCatalog, SaveConfiguration),
            new StateController(playerState),
            new PluginController(commandDispatcher, pluginService),
            new ScreenshotController(screenshotModule),
            new StreamController(configuration, webSocketHub, screenshotModule, SaveConfiguration),
            new ShortcutController(configuration, SaveConfiguration),
            new SettingsController(configuration, SaveConfiguration),
            new DailyRoutinesController(dailyRoutines),
            new CommandController(commandDispatcher),
        };

        commandDispatcher.OnStartStream = fps =>
        {
            configuration.StreamFps = fps;
            SaveConfiguration();
            return screenshotModule.StartStreamingAsync(fps,
                frame => webSocketHub.BroadcastBinaryAsync(frame, CancellationToken.None));
        };
        commandDispatcher.OnStopStream = () => screenshotModule.StopStreamingAsync();

        webServer = new LanWebServer(
            configuration, eventBus, webSocketHub, webSocketHandler, controllers, staticRoot);

        configWindow = new PocketStationConfigWindow(
            configuration,
            SaveConfiguration,
            RestartServer,
            () => webServer.ClientCount,
            () => webServer.AccessUrls);

        var webView2DataFolder = Path.Combine(configDirectory, "WebView2Data");
        floatingWindow = new PocketStationFloatingWindow(
            configuration,
            SaveConfiguration,
            OpenConfigUi,
            () => $"http://127.0.0.1:{configuration.Port}/?token={Uri.EscapeDataString(configuration.Token)}",
            webView2DataFolder);

        if (configuration.LanEnabled)
            StartServer();

        pluginInterface.UiBuilder.Draw += configWindow.Draw;
        pluginInterface.UiBuilder.Draw += floatingWindow.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        RegisterCommand(CommandName);
        RegisterCommand(ShortCommandName);

        eventBus.Publish("event.system", new Domain.SystemEvent("info", "Pocket Station initialized", new
        {
            lanEnabled = configuration.LanEnabled,
            urls = webServer.AccessUrls
        }));

        PocketBackendClient.QueueHeartbeat(configuration, "startup", new
        {
            lanEnabled = configuration.LanEnabled,
            port = configuration.Port,
        });
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(ShortCommandName);
        pluginInterface.UiBuilder.Draw -= configWindow.Draw;
        pluginInterface.UiBuilder.Draw -= floatingWindow.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

        floatingWindow.Dispose();
        webServer.Dispose();
        moduleHost.Dispose();
        game.Dispose();
        DService.Uninit();
        SaveConfiguration();
    }

    private void OpenConfigUi()
    {
        configWindow.Open();
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("capture", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("screenshot", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("screen", StringComparison.OrdinalIgnoreCase))
        {
            _ = CaptureFromCommandAsync();
            return;
        }

        if (trimmed.Equals("sendtest", StringComparison.OrdinalIgnoreCase))
        {
            _ = game.SendChatOrCommandAsync("/e [Pocket Station] send test");
            return;
        }

        if (trimmed.Equals("floating", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("float", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("icon", StringComparison.OrdinalIgnoreCase))
        {
            configuration.ShowFloatingButton = !configuration.ShowFloatingButton;
            SaveConfiguration();
            DService.Instance().Chat.Print(
                $"[Pocket Station] 悬浮按钮已{(configuration.ShowFloatingButton ? "显示" : "隐藏")}。");
            return;
        }

        configWindow.Open();
    }

    private void RegisterCommand(string command)
    {
        commandManager.AddHandler(command, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 Pocket Station 局域网控制台设置。"
        });
    }

    private async Task CaptureFromCommandAsync()
    {
        try
        {
            var result = await screenshotModule.CaptureAsync(CancellationToken.None).ConfigureAwait(false);
            DService.Instance().Chat.Print($"[Pocket Station] 截图已推送：{result.Width}x{result.Height}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to capture screenshot from command");
            DService.Instance().Chat.PrintError($"[Pocket Station] 截图失败：{ex.Message}");
        }
    }

    private void RestartServer()
    {
        webServer.StopAsync().GetAwaiter().GetResult();
        if (configuration.LanEnabled)
            StartServer();
    }

    private void StartServer()
    {
        var result = webServer.Start();
        if (!result.Started)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                DService.Instance().Chat.PrintError($"[Pocket Station] 局域网服务器启动失败：{result.ErrorMessage}");
            return;
        }

        if (!result.PortChanged)
            return;

        SaveConfiguration();
        DService.Instance().Chat.Print(
            $"[Pocket Station] 端口 {result.RequestedPort} 被占用，已自动切换到 {result.ActualPort}。");
    }

    private void SaveConfiguration()
    {
        pluginInterface.SavePluginConfig(configuration);
    }
}
