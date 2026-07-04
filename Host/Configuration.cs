using System.Numerics;
using Dalamud.Configuration;
using PocketStation;
using PocketStation.Domain;
using PocketStation.Infrastructure.Network;

namespace PocketStation.Host;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Tracks which version of <see cref="CommandShortcutDefaults"/> has been merged.
    /// When less than <see cref="CommandShortcutDefaults.CurrentVersion"/>,
    /// <see cref="CommandShortcutDefaults.EnsureDefaults"/> will merge any new built-in
    /// shortcuts the user doesn't already have.
    /// </summary>
    public int ShortcutDefaultsVersion { get; set; }

    public bool LanEnabled { get; set; } = true;
    public bool RequireToken { get; set; } = true;
    public int Port { get; set; } = 8787;
    public string Token { get; set; } = AuthToken.Create();
    public string InstallId { get; set; } = Guid.NewGuid().ToString("N");
    public bool EnablePocketBackendTelemetry { get; set; } = true;

    public int MaxClients { get; set; } = 8;
    public int ChatHistoryLimit { get; set; } = 500;
    public int PlayerStateIntervalMs { get; set; } = 750;
    public int ScreenshotJpegQuality { get; set; } = 75;
    public int StreamFps { get; set; } = 30;
    public bool ShowFloatingButton { get; set; } = true;
    public Vector2 FloatingButtonPosition { get; set; } = new(48f, 180f);
    public bool HasFloatingButtonPosition { get; set; }

    public string UiTheme { get; set; } = "velvet";
    public string SelectedChatModeId { get; set; } = ChatFilterDefaults.AllId;
    public List<ChatFilterMode> ChatFilterModes { get; set; } = [];
    public List<CommandShortcut> CommandShortcuts { get; set; } = [];

    public void Normalize()
    {
        if (Port is < 1024 or > 65535)
            Port = 8787;

        if (string.IsNullOrWhiteSpace(Token) || Token.Length < 16)
            Token = AuthToken.Create();

        if (string.IsNullOrWhiteSpace(InstallId) || InstallId.Length < 8)
            InstallId = Guid.NewGuid().ToString("N");

        if (MaxClients < 1)
            MaxClients = 1;

        if (ChatHistoryLimit < 50)
            ChatHistoryLimit = 50;

        if (PlayerStateIntervalMs < 250)
            PlayerStateIntervalMs = 250;

        if (ScreenshotJpegQuality is < 20 or > 95)
            ScreenshotJpegQuality = 75;

        if (StreamFps is < 1 or > 120)
            StreamFps = 30;

        if (!IsValidUiTheme(UiTheme))
            UiTheme = "velvet";

        ChatFilterDefaults.EnsureDefaults(this);

        CommandShortcutDefaults.EnsureDefaults(this);
    }

    private static bool IsValidUiTheme(string? theme) =>
        theme is "phantom" or "spotlight" or "velvet" or "neon" or "afterlife" or "braindance";
}
