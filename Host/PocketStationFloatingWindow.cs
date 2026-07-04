using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using OmenTools;

namespace PocketStation.Host;

/// <summary>
/// A small floating icon button overlay. Left-click opens a phone-sized
/// WebView2 window showing the Pocket Station web UI.
/// Right-click drag to move; right-click for context menu.
/// </summary>
internal sealed class PocketStationFloatingWindow : IDisposable
{
    private const float ButtonSize = 58f;
    private const float WindowPadding = 7f;

    private readonly Configuration _configuration;
    private readonly Action _saveConfiguration;
    private readonly Action _openConfig;
    private readonly Func<string> _getUrl;
    private readonly string _webView2DataFolder;

    private ISharedImmediateTexture? _floatingIconTexture;
    private PhoneWebViewForm? _webForm;

    // Right-drag state (mirrors FloatingRecordWindow logic)
    private bool _wasDragging;
    private bool _rightDragStartedOnButton;
    private Vector2 _rightDragStartMousePos;
    private Vector2 _rightDragStartWindowPos;
    private Vector2? _rightDragCurrentWindowPos;
    private bool _rightClickRequestedMenu;
    private bool _suppressContextMenuThisFrame;

    public bool IsOpen { get; set; }

    public PocketStationFloatingWindow(
        Configuration configuration,
        Action saveConfiguration,
        Action openConfig,
        Func<string> getUrl,
        string webView2DataFolder)
    {
        _configuration = configuration;
        _saveConfiguration = saveConfiguration;
        _openConfig = openConfig;
        _getUrl = getUrl;
        _webView2DataFolder = webView2DataFolder;
        IsOpen = configuration.ShowFloatingButton;
    }

    public void Draw()
    {
        IsOpen = _configuration.ShowFloatingButton;
        if (!IsOpen)
            return;

        if (_rightDragCurrentWindowPos is { } dragPosition)
            ImGui.SetNextWindowPos(dragPosition, ImGuiCond.Always);
        else if (_configuration.HasFloatingButtonPosition)
            ImGui.SetNextWindowPos(_configuration.FloatingButtonPosition, ImGuiCond.Always);

        var totalSize = new Vector2(ButtonSize + WindowPadding * 2);
        ImGui.SetNextWindowSize(totalSize, ImGuiCond.Always);

        var flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoBackground;

        if (!ImGui.Begin("###PocketStationFloating", flags))
        {
            ImGui.End();
            return;
        }

        _suppressContextMenuThisFrame = false;
        bool pressed = DrawIconButton();

        if (pressed)
            OpenWebView();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("- 左键打开 Pocket Station\n- 右键单击打开菜单\n- 右键按住拖动");
            ImGui.EndTooltip();
        }

        HandleRightMouseDrag();

        if (_rightClickRequestedMenu)
        {
            ImGui.OpenPopup("###PocketStationFloatingMenu");
            _rightClickRequestedMenu = false;
        }

        if (!_suppressContextMenuThisFrame &&
            ImGui.BeginPopup("###PocketStationFloatingMenu"))
        {
            if (ImGui.MenuItem("打开设置"))
                _openConfig();

            if (ImGui.MenuItem("重置位置"))
            {
                _configuration.FloatingButtonPosition = new Vector2(48f, 180f);
                _configuration.HasFloatingButtonPosition = true;
                _saveConfiguration();
            }

            bool show = _configuration.ShowFloatingButton;
            if (ImGui.MenuItem("显示悬浮按钮", string.Empty, show))
            {
                _configuration.ShowFloatingButton = !show;
                _saveConfiguration();
            }

            ImGui.EndPopup();
        }

        ImGui.End();
    }

    private bool DrawIconButton()
    {
        if (_floatingIconTexture == null)
        {
            var iconPath = Path.Combine(
                Plugin.PluginInterface.AssemblyLocation.Directory?.FullName ?? AppContext.BaseDirectory,
                "floating-icon.png");
            _floatingIconTexture = DService.Instance().Texture.GetFromFile(iconPath);
        }

        var wrap = _floatingIconTexture.GetWrapOrEmpty();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        try
        {
            ImGui.SetCursorPos(new Vector2(WindowPadding, WindowPadding));
            ImGui.InvisibleButton("###PocketStationButton", new Vector2(ButtonSize, ButtonSize));

            bool pressed = ImGui.IsItemClicked(ImGuiMouseButton.Left);
            bool hovered = ImGui.IsItemHovered();
            bool held = ImGui.IsItemActive();

            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var center = (min + max) * 0.5f;
            var draw = ImGui.GetWindowDrawList();

            uint shadow = Color(0.00f, 0.00f, 0.00f, 0.45f);
            uint shell = held
                ? Color(0.04f, 0.09f, 0.15f, 0.98f)
                : Color(0.03f, 0.05f, 0.10f, 0.95f);
            uint panel = Color(0.05f, 0.11f, 0.18f, hovered ? 0.98f : 0.90f);
            uint cyan = Color(0.05f, hovered ? 0.95f : 0.80f, 0.95f, 1f);
            uint magenta = Color(0.92f, 0.15f, 0.75f, hovered ? 0.95f : 0.62f);
            uint green = Color(0.08f, 0.92f, 0.58f, 1f);

            draw.AddRectFilled(min + new Vector2(3f, 4f), max + new Vector2(3f, 4f), shadow, 16f);
            draw.AddRectFilled(min, max, shell, 16f);
            draw.AddRect(min + new Vector2(0.5f, 0.5f), max - new Vector2(0.5f, 0.5f), cyan, 16f, ImDrawFlags.None, 1.35f);
            draw.AddRect(min + new Vector2(4f, 4f), max - new Vector2(4f, 4f), magenta, 12f, ImDrawFlags.None, 1.1f);

            var panelMin = min + new Vector2(10f, 10f);
            var panelMax = max - new Vector2(10f, 10f);
            draw.AddRectFilled(panelMin, panelMax, panel, 10f);
            DrawCornerTicks(draw, panelMin, panelMax, cyan, magenta);

            if (wrap.Handle != nint.Zero)
            {
                var iconInset = hovered ? 0f : 0.5f;
                draw.AddImage(wrap.Handle, panelMin + new Vector2(iconInset), panelMax - new Vector2(iconInset), Vector2.Zero, Vector2.One);
            }
            else
            {
                draw.AddCircleFilled(center, 9.5f, green, 32);
                draw.AddCircle(center, 15.5f, Color(0.08f, 0.92f, 0.58f, 0.44f), 32, 2.2f);
            }

            return pressed;
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    private void OpenWebView()
    {
        if (_webForm != null && !_webForm.IsDisposed)
        {
            _webForm.BringToFront();
            _webForm.Activate();
            return;
        }

        var url = _getUrl();
        if (string.IsNullOrEmpty(url))
        {
            _openConfig();
            return;
        }

        try
        {
            _webForm = new PhoneWebViewForm(url, _webView2DataFolder);
            _webForm.Show();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to open WebView2 window");
        }
    }

    private void HandleRightMouseDrag()
    {
        if (!_rightDragStartedOnButton)
        {
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) &&
                ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _rightDragStartedOnButton = true;
                _wasDragging = false;
                _rightDragStartMousePos = ImGui.GetMousePos();
                _rightDragStartWindowPos = ImGui.GetWindowPos();
                _rightDragCurrentWindowPos = null;
            }

            return;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            if (_wasDragging)
            {
                _suppressContextMenuThisFrame = true;
                _configuration.FloatingButtonPosition = _rightDragCurrentWindowPos ?? ImGui.GetWindowPos();
                _configuration.HasFloatingButtonPosition = true;
                _saveConfiguration();
            }

            _rightDragStartedOnButton = false;
            _wasDragging = false;
            _rightDragCurrentWindowPos = null;
            if (!_suppressContextMenuThisFrame)
                _rightClickRequestedMenu = true;
            return;
        }

        var dragDelta = ImGui.GetMousePos() - _rightDragStartMousePos;
        if (!_wasDragging && dragDelta.LengthSquared() < 16f)
            return;

        _wasDragging = true;
        _rightDragCurrentWindowPos = _rightDragStartWindowPos + dragDelta;
    }

    private static uint Color(float r, float g, float b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

    private static void DrawCornerTicks(ImDrawListPtr draw, Vector2 min, Vector2 max, uint cyan, uint magenta)
    {
        const float length = 8f;
        const float thickness = 1.8f;

        draw.AddLine(min + new Vector2(2f, 0f), min + new Vector2(length, 0f), cyan, thickness);
        draw.AddLine(min + new Vector2(0f, 2f), min + new Vector2(0f, length), cyan, thickness);

        draw.AddLine(new Vector2(max.X - length, min.Y), new Vector2(max.X - 2f, min.Y), magenta, thickness);
        draw.AddLine(new Vector2(max.X, min.Y + 2f), new Vector2(max.X, min.Y + length), magenta, thickness);

        draw.AddLine(new Vector2(min.X + 2f, max.Y), new Vector2(min.X + length, max.Y), magenta, thickness);
        draw.AddLine(new Vector2(min.X, max.Y - length), new Vector2(min.X, max.Y - 2f), magenta, thickness);

        draw.AddLine(max - new Vector2(length, 0f), max - new Vector2(2f, 0f), cyan, thickness);
        draw.AddLine(max - new Vector2(0f, length), max - new Vector2(0f, 2f), cyan, thickness);
    }

    public void Dispose()
    {
        _webForm?.Dispose();
        _webForm = null;
    }
}
