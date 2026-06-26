using System.Collections;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using QRCoder;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.Dalamud.Helpers;
using PocketStation.Core;
using PocketStation.Game;
using PocketStation.Helpers;
using PocketStation.Modules;
using PocketStation.Web;

namespace PocketStation;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pocketstation";
    private const string ShortCommandName = "/ps";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        WriteIndented = false
    };

    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly GameFacade game;
    private readonly PocketModuleHost moduleHost;
    private readonly ChatMonitorModule chatMonitor;
    private readonly PlayerStateModule playerState;
    private readonly ScreenshotModule screenshotModule;
    private readonly CommandDispatcher commandDispatcher;
    private readonly LanWebServer webServer;

    private bool showConfig;
    private bool disposed;

    // QR code cache — regenerated when access URLs change
    private bool[,]? _qrMatrix;
    private string? _qrUrl;

    public Plugin()
    {
        DService.Init(PluginInterface);

        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();
        SaveConfiguration();

        eventBus = new EventBus();
        game = new GameFacade(ChatGui, CommandManager, ClientState, ObjectTable, PartyList, Framework);
        game.Initialize();

        var configDirectory = PluginInterface.GetPluginConfigDirectory();
        screenshotModule = new ScreenshotModule(configuration, eventBus, configDirectory);
        var dailyRoutines = new DailyRoutinesService(configDirectory);
        chatMonitor = new ChatMonitorModule(configuration, eventBus, game);
        playerState = new PlayerStateModule(configuration, eventBus, game, Framework);
        commandDispatcher = new CommandDispatcher(configuration, eventBus, game, screenshotModule);
        commandDispatcher.OnTogglePlugin = DalamudReflectorEx.SetPluginStateAsync;

        moduleHost = new PocketModuleHost();
        moduleHost.Add(chatMonitor);
        moduleHost.Add(playerState);
        moduleHost.Add(screenshotModule);
        moduleHost.Initialize();

        var staticRoot = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName ?? AppContext.BaseDirectory, "wwwroot");
        webServer = new LanWebServer(
            configuration,
            eventBus,
            commandDispatcher,
            chatMonitor,
            playerState,
            screenshotModule,
            dailyRoutines,
            staticRoot,
            SaveConfiguration);

        if (configuration.LanEnabled)
            webServer.Start();

        PluginInterface.UiBuilder.Draw += DrawConfigUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;

        RegisterCommand(CommandName);
        RegisterCommand(ShortCommandName);

        eventBus.Publish("event.system", new Protocol.SystemEvent("info", "Pocket Station initialized", new
        {
            lanEnabled = configuration.LanEnabled,
            urls = webServer.AccessUrls
        }));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
        PluginInterface.UiBuilder.Draw -= DrawConfigUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;

        webServer.Dispose();
        moduleHost.Dispose();
        game.Dispose();
        DService.Uninit();
        SaveConfiguration();
    }

    private void OpenConfigUi()
    {
        showConfig = true;
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

        showConfig = true;
    }

    private void RegisterCommand(string command)
    {
        CommandManager.AddHandler(command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Pocket Station LAN console settings."
        });
    }

    private async Task CaptureFromCommandAsync()
    {
        try
        {
            var result = await screenshotModule.CaptureAsync(CancellationToken.None).ConfigureAwait(false);
            DService.Instance().Chat.Print($"[Pocket Station] Screenshot pushed: {result.Width}x{result.Height}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to capture screenshot from command");
            DService.Instance().Chat.PrintError($"[Pocket Station] Screenshot failed: {ex.Message}");
        }
    }

    private void DrawConfigUi()
    {
        if (!showConfig)
            return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(520, 680), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Pocket Station", ref showConfig))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("LAN mobile console");
        ImGui.Separator();

        var lanEnabled = configuration.LanEnabled;
        if (ImGui.Checkbox("Enable LAN server", ref lanEnabled))
        {
            configuration.LanEnabled = lanEnabled;
            SaveConfiguration();
            RestartServer();
        }

        var requireToken = configuration.RequireToken;
        if (ImGui.Checkbox("Require token", ref requireToken))
        {
            configuration.RequireToken = requireToken;
            SaveConfiguration();
        }

        var port = configuration.Port;
        if (ImGui.InputInt("Port", ref port))
        {
            configuration.Port = port;
            configuration.Normalize();
            SaveConfiguration();
        }

        if (ImGui.Button("Restart server"))
            RestartServer();

        ImGui.SameLine();
        if (ImGui.Button("Rotate token"))
        {
            configuration.Token = AuthToken.Create();
            SaveConfiguration();
            RestartServer();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Live Stream");
        var streamFps = configuration.StreamFps;
        if (ImGui.SliderInt("Stream FPS", ref streamFps, 1, 120))
        {
            configuration.StreamFps = streamFps;
            SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextUnformatted($"Clients: {webServer.ClientCount}");
        ImGui.TextUnformatted($"Token: {configuration.Token}");

        foreach (var url in webServer.AccessUrls)
        {
            ImGui.TextWrapped(url);
            ImGui.SameLine();
            if (ImGui.SmallButton($"Copy##{url}"))
                ImGui.SetClipboardText(url);
        }

        ImGui.Separator();

        // ── QR code ──────────────────────────────────────────
        var accessUrls = webServer.AccessUrls;
        if (accessUrls.Count > 0)
        {
            ImGui.TextUnformatted("Scan to connect");
            var firstUrl = accessUrls[0];

            // Regenerate QR only when URL changes (using QRCoder)
            if (_qrMatrix == null || _qrUrl != firstUrl)
            {
                _qrUrl = firstUrl;
                _qrMatrix = GenerateQrMatrix(firstUrl);
            }

            DrawQrCode(_qrMatrix, 200f);
        }

        // ── Command shortcut management ──────────────────────
        ShortcutManagerUi.Draw(configuration.CommandShortcuts, SaveConfiguration);

        ImGui.Separator();
        ImGui.TextWrapped("LAN Web can receive chat, send chat or commands, request screenshots, and save custom chat filter modes.");

        ImGui.End();
    }

    internal static List<Protocol.DalamudPluginInfo> GetInstalledPlugins()
    {
        return PluginInterface.InstalledPlugins
            .Select(p => new Protocol.DalamudPluginInfo(
                p.InternalName,
                p.Name,
                p.Version?.ToString() ?? "?",
                p.IsLoaded))
            .ToList();
    }

    private void RestartServer()
    {
        webServer.StopAsync().GetAwaiter().GetResult();
        if (configuration.LanEnabled)
            webServer.Start();
    }

    private void SaveConfiguration()
    {
        PluginInterface.SavePluginConfig(configuration);
    }

    /// <summary>Generate QR matrix using QRCoder library.</summary>
    private static bool[,] GenerateQrMatrix(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        int size = matrix.Count;
        var result = new bool[size, size];
        for (int r = 0; r < size; r++)
        {
            var row = matrix[r];
            for (int c = 0; c < size; c++)
                result[r, c] = row[c];
        }
        return result;
    }

    /// <summary>Render a QR code matrix using ImGui draw list.</summary>
    private static void DrawQrCode(bool[,] matrix, float maxSize)
    {
        int size = matrix.GetLength(0);
        const int quietZone = 4; // modules of white border on each side

        float available = maxSize;
        float moduleSize = available / (size + quietZone * 2);
        float totalSize = moduleSize * (size + quietZone * 2);

        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        // White background (quiet zone)
        drawList.AddRectFilled(cursor, cursor + new Vector2(totalSize), 0xFFFFFFFF);

        // Dark modules
        uint darkColor = 0xFF000000;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (matrix[r, c])
                {
                    float x = cursor.X + (c + quietZone) * moduleSize;
                    float y = cursor.Y + (r + quietZone) * moduleSize;
                    drawList.AddRectFilled(
                        new Vector2(x, y),
                        new Vector2(x + moduleSize, y + moduleSize),
                        darkColor);
                }
            }
        }

        // Advance cursor past the rendered area
        ImGui.Dummy(new Vector2(totalSize, totalSize));
    }
}
