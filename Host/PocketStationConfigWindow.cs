using System.Numerics;
using Dalamud.Bindings.ImGui;
using QRCoder;

namespace PocketStation.Host;

internal sealed class PocketStationConfigWindow
{
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly Action restartServer;
    private readonly Func<int> getClientCount;
    private readonly Func<IReadOnlyList<string>> getAccessUrls;

    private bool isOpen;
    private bool[,]? qrMatrix;
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
        DrawStreamSettings();
        DrawAccessInfo();
        ShortcutManagerUi.Draw(configuration.CommandShortcuts, saveConfiguration);

        ImGui.End();
    }

    private void DrawNetworkSettings()
    {
        ImGui.TextUnformatted("局域网移动控制台");
        ImGui.Separator();

        var lanEnabled = configuration.LanEnabled;
        if (ImGui.Checkbox("启用局域网服务器", ref lanEnabled))
        {
            configuration.LanEnabled = lanEnabled;
            saveConfiguration();
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
        }

        if (ImGui.Button("重启服务器"))
            restartServer();

        ImGui.SameLine();
        if (ImGui.Button("更换令牌"))
        {
            configuration.Token = Infrastructure.Network.AuthToken.Create();
            saveConfiguration();
            restartServer();
        }
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

        var accessUrls = getAccessUrls();
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
        if (qrMatrix == null || qrUrl != firstUrl)
        {
            qrUrl = firstUrl;
            qrMatrix = GenerateQrMatrix(firstUrl);
        }

        DrawQrCode(qrMatrix, 200f);
    }

    private static bool[,] GenerateQrMatrix(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var result = new bool[size, size];
        for (var rowIndex = 0; rowIndex < size; rowIndex++)
        {
            var row = matrix[rowIndex];
            for (var columnIndex = 0; columnIndex < size; columnIndex++)
                result[rowIndex, columnIndex] = row[columnIndex];
        }

        return result;
    }

    private static void DrawQrCode(bool[,] matrix, float maxSize)
    {
        var size = matrix.GetLength(0);
        const int quietZone = 4;

        var moduleSize = maxSize / (size + quietZone * 2);
        var totalSize = moduleSize * (size + quietZone * 2);

        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(cursor, cursor + new Vector2(totalSize), 0xFFFFFFFF);

        const uint darkColor = 0xFF000000;
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                if (!matrix[row, column])
                    continue;

                var x = cursor.X + (column + quietZone) * moduleSize;
                var y = cursor.Y + (row + quietZone) * moduleSize;
                drawList.AddRectFilled(
                    new Vector2(x, y),
                    new Vector2(x + moduleSize, y + moduleSize),
                    darkColor);
            }
        }

        ImGui.Dummy(new Vector2(totalSize, totalSize));
    }
}
