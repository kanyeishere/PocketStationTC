using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PocketStation.Core;
using PocketStation.Protocol;

namespace PocketStation.Modules;

public sealed class ScreenshotModule : IGameModule
{
    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim captureLock = new(1, 1);

    private string? latestPath;
    private int latestWidth;
    private int latestHeight;

    public string Name => "Screenshot";

    public ScreenshotModule(Configuration configuration, EventBus eventBus, string pluginConfigDirectory)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        cacheDirectory = Path.Combine(pluginConfigDirectory, "web-cache");
    }

    public void Initialize()
    {
        Directory.CreateDirectory(cacheDirectory);
    }

    public string? LatestPath => latestPath;

    public async Task<ScreenshotReadyEvent> CaptureAsync(CancellationToken cancellationToken)
    {
        Plugin.Log.Info("Screenshot capture requested");
        await captureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(CaptureInternal, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            captureLock.Release();
        }
    }

    public void Dispose()
    {
        captureLock.Dispose();
    }

    private ScreenshotReadyEvent CaptureInternal()
    {
        var hwnd = Plugin.PluginInterface.UiBuilder.WindowHandlePtr;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Game window handle is unavailable.");

        if (!GetClientRect(hwnd, out var rect))
            throw new InvalidOperationException("Failed to read game client rectangle.");

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var origin = new NativePoint { X = 0, Y = 0 };

        if (!ClientToScreen(hwnd, ref origin))
            throw new InvalidOperationException("Failed to map game client rectangle to screen.");

        var tempPath = Path.Combine(cacheDirectory, "latest.tmp.jpg");
        var finalPath = Path.Combine(cacheDirectory, "latest.jpg");

        using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            SaveJpeg(bitmap, tempPath, configuration.ScreenshotJpegQuality);
        }

        if (File.Exists(finalPath))
            File.Delete(finalPath);

        File.Move(tempPath, finalPath);

        latestPath = finalPath;
        latestWidth = width;
        latestHeight = height;

        var evt = new ScreenshotReadyEvent(
            "/api/screen/latest.jpg",
            latestWidth,
            latestHeight,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            "image/jpeg");

        Plugin.Log.Info("Screenshot captured: {Width}x{Height}, {Path}", latestWidth, latestHeight, finalPath);
        eventBus.Publish("event.screen.ready", evt);
        return evt;
    }

    private static void SaveJpeg(Image image, string path, int quality)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.FormatID == ImageFormat.Jpeg.Guid);
        if (encoder == null)
        {
            image.Save(path, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        image.Save(path, encoder, parameters);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
