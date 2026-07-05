using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using QRCoder;

namespace PocketStation.Host;

internal sealed class PocketStationConfigWindow
{
    private const string DiscordInviteUrl = "https://discord.gg/CQd4w7Bzv2";
    private const long AccessUrlRefreshIntervalMs = 2_000;

    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly Action restartServer;
    private readonly Func<int> getClientCount;
    private readonly Func<IReadOnlyList<string>> getAccessUrls;

    private bool isOpen;
    private IReadOnlyList<string> cachedAccessUrls = Array.Empty<string>();
    private int cachedAccessUrlsPort;
    private string? cachedAccessUrlsToken;
    private bool cachedAccessUrlsLanEnabled;
    private long nextAccessUrlRefreshTicks;
    private QrCodeRenderData? qrCode;
    private string? qrUrl;

    public PocketStationConfigWindow(
        Configuration configuration,
        Action saveConfiguration,
        Action restartServer,
        Func<int> getClientCount,
        Func<IReadOnlyList<string>> getAccessUrls)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.restartServer = restartServer;
        this.getClientCount = getClientCount;
        this.getAccessUrls = getAccessUrls;
    }

    public void Open()
    {
        isOpen = true;
    }

    public void Draw()
    {
        if (!isOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(520, 680), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Pocket Station", ref isOpen))
        {
            ImGui.End();
            return;
        }

        DrawNetworkSettings();
        DrawFloatingButtonSettings();
        DrawStreamSettings();
        DrawAccessInfo();
        ShortcutManagerUi.Draw(configuration.CommandShortcuts, saveConfiguration);

        ImGui.End();
    }

    private void DrawNetworkSettings()
    {
        ImGui.TextUnformatted("局域网移动控制台");
        DrawCommunityLink();
        ImGui.Separator();

        var lanEnabled = configuration.LanEnabled;
        if (ImGui.Checkbox("启用局域网服务器", ref lanEnabled))
        {
            configuration.LanEnabled = lanEnabled;
            saveConfiguration();
            InvalidateAccessInfo();
            restartServer();
        }

        var requireToken = configuration.RequireToken;
        if (ImGui.Checkbox("需要令牌", ref requireToken))
        {
            configuration.RequireToken = requireToken;
            saveConfiguration();
        }

        var port = configuration.Port;
        if (ImGui.InputInt("端口", ref port))
        {
            configuration.Port = port;
            configuration.Normalize();
            saveConfiguration();
            InvalidateAccessInfo();
        }

        if (ImGui.Button("重启服务器"))
        {
            InvalidateAccessInfo();
            restartServer();
        }

        ImGui.SameLine();
        if (ImGui.Button("更换令牌"))
        {
            configuration.Token = Infrastructure.Network.AuthToken.Create();
            saveConfiguration();
            InvalidateAccessInfo();
            restartServer();
        }
    }

    private static void DrawCommunityLink()
    {
        ImGui.Spacing();
        if (ImGui.Button("加入 Discord 社群", new Vector2(-1, 0)))
            OpenDiscordInvite();

        ImGui.TextDisabled(DiscordInviteUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("复制链接##discord"))
            ImGui.SetClipboardText(DiscordInviteUrl);
    }

    private void DrawFloatingButtonSettings()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("悬浮按钮");

        var showFloating = configuration.ShowFloatingButton;
        if (ImGui.Checkbox("显示悬浮按钮", ref showFloating))
        {
            configuration.ShowFloatingButton = showFloating;
            saveConfiguration();
        }

        ImGui.SameLine();
        if (ImGui.Button("重置位置"))
        {
            configuration.FloatingButtonPosition = new Vector2(48f, 180f);
            configuration.HasFloatingButtonPosition = true;
            saveConfiguration();
        }

        ImGui.TextDisabled("左键打开手机窗口，右键拖动移动，右键单击打开菜单。");
    }

    private void DrawStreamSettings()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("实时串流");

        var streamFps = configuration.StreamFps;
        if (ImGui.SliderInt("串流帧率", ref streamFps, 1, 120))
        {
            configuration.StreamFps = streamFps;
            saveConfiguration();
        }
    }

    private void DrawAccessInfo()
    {
        ImGui.Separator();
        ImGui.TextUnformatted($"已连接客户端：{getClientCount()}");
        ImGui.TextUnformatted($"令牌：{configuration.Token}");

        var accessUrls = GetCachedAccessUrls();
        foreach (var url in accessUrls)
        {
            ImGui.TextWrapped(url);
            ImGui.SameLine();
            if (ImGui.SmallButton($"复制##{url}"))
                ImGui.SetClipboardText(url);
        }

        ImGui.Separator();
        if (accessUrls.Count == 0)
            return;

        ImGui.TextUnformatted("扫码连接");
        var firstUrl = accessUrls[0];
        if (qrCode == null || qrUrl != firstUrl)
        {
            qrUrl = firstUrl;
            qrCode = GenerateQrCode(firstUrl);
        }

        DrawQrCode(qrCode, 200f);
    }

    private IReadOnlyList<string> GetCachedAccessUrls()
    {
        var now = Environment.TickCount64;
        var settingsChanged =
            cachedAccessUrlsPort != configuration.Port ||
            cachedAccessUrlsLanEnabled != configuration.LanEnabled ||
            !string.Equals(cachedAccessUrlsToken, configuration.Token, StringComparison.Ordinal);

        if (settingsChanged || cachedAccessUrls.Count == 0 || now >= nextAccessUrlRefreshTicks)
        {
            cachedAccessUrls = getAccessUrls();
            cachedAccessUrlsPort = configuration.Port;
            cachedAccessUrlsLanEnabled = configuration.LanEnabled;
            cachedAccessUrlsToken = configuration.Token;
            nextAccessUrlRefreshTicks = now + AccessUrlRefreshIntervalMs;
        }

        return cachedAccessUrls;
    }

    private void InvalidateAccessInfo()
    {
        cachedAccessUrls = Array.Empty<string>();
        nextAccessUrlRefreshTicks = 0;
        qrCode = null;
        qrUrl = null;
    }

    private static QrCodeRenderData GenerateQrCode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var runs = new List<QrRun>();

        for (var rowIndex = 0; rowIndex < size; rowIndex++)
        {
            var row = matrix[rowIndex];
            var runStart = -1;

            for (var columnIndex = 0; columnIndex <= size; columnIndex++)
            {
                var isDark = columnIndex < size && row[columnIndex];
                if (isDark)
                {
                    if (runStart < 0)
                        runStart = columnIndex;
                }
                else if (runStart >= 0)
                {
                    runs.Add(new QrRun(rowIndex, runStart, columnIndex));
                    runStart = -1;
                }
            }
        }

        return new QrCodeRenderData(size, runs.ToArray());
    }

    private static void DrawQrCode(QrCodeRenderData qrCode, float maxSize)
    {
        var size = qrCode.Size;
        const int quietZone = 4;

        var moduleSize = maxSize / (size + quietZone * 2);
        var totalSize = moduleSize * (size + quietZone * 2);

        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(cursor, cursor + new Vector2(totalSize), 0xFFFFFFFF);

        const uint darkColor = 0xFF000000;
        foreach (var run in qrCode.Runs)
        {
            var x0 = cursor.X + (run.StartColumn + quietZone) * moduleSize;
            var y0 = cursor.Y + (run.Row + quietZone) * moduleSize;
            var x1 = cursor.X + (run.EndColumn + quietZone) * moduleSize;
            drawList.AddRectFilled(
                new Vector2(x0, y0),
                new Vector2(x1, y0 + moduleSize),
                darkColor);
        }

        ImGui.Dummy(new Vector2(totalSize, totalSize));
    }

    private sealed record QrCodeRenderData(int Size, IReadOnlyList<QrRun> Runs);

    private readonly record struct QrRun(int Row, int StartColumn, int EndColumn);

    private static void OpenDiscordInvite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DiscordInviteUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to open Discord invite: {ex}");
        }
    }
}
