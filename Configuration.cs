using Dalamud.Configuration;
using PocketStation.Protocol;

namespace PocketStation;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool LanEnabled { get; set; } = true;
    public bool RequireToken { get; set; } = true;
    public int Port { get; set; } = 8787;
    public string Token { get; set; } = Web.AuthToken.Create();

    public int MaxClients { get; set; } = 8;
    public int ChatHistoryLimit { get; set; } = 500;
    public int PlayerStateIntervalMs { get; set; } = 750;
    public int ScreenshotJpegQuality { get; set; } = 75;
    public int StreamFps { get; set; } = 30;
    public string SelectedChatModeId { get; set; } = ChatFilterDefaults.AllId;
    public List<ChatFilterMode> ChatFilterModes { get; set; } = [];
    public List<CommandShortcut> CommandShortcuts { get; set; } = [];

    public void Normalize()
    {
        if (Port is < 1024 or > 65535)
            Port = 8787;

        if (string.IsNullOrWhiteSpace(Token) || Token.Length < 16)
            Token = Web.AuthToken.Create();

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

        ChatFilterDefaults.EnsureDefaults(this);

        CommandShortcutDefaults.EnsureDefaults(this);
    }
}
