using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PocketStation.Host;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Domain;

namespace PocketStation.Services;

public sealed class ScreenshotModule : IGameModule
{
    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim captureLock = new(1, 1);

    private string? latestPath;
    private int latestWidth;
    private int latestHeight;

    private CancellationTokenSource? streamCts;
    private Task? streamTask;
    private Func<byte[], Task>? frameCallback;
    private bool isStreaming;

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

        // Clean up leftover temp files from aborted captures
        foreach (var tmp in Directory.GetFiles(cacheDirectory, "*.tmp.*"))
        {
            try
            {
                File.Delete(tmp);
                Plugin.Log.Debug("Cleaned up leftover temp file: {Path}", tmp);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Failed to delete leftover temp file: {Path}", tmp);
            }
        }
    }

    public string? LatestPath => latestPath;

    public bool IsStreaming => isStreaming;

    public async Task<ScreenshotReadyEvent> CaptureAsync(CancellationToken cancellationToken)
    {
        Plugin.Log.Info("Screenshot capture requested");
        await captureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(CaptureToFile, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            captureLock.Release();
        }
    }

    public async Task StartStreamingAsync(int fps, Func<byte[], Task> onFrame)
    {
        await StopStreamingAsync().ConfigureAwait(false);

        fps = Math.Clamp(fps, 1, 120);
        frameCallback = onFrame;
        streamCts = new CancellationTokenSource();
        var token = streamCts.Token;
        isStreaming = true;

        var delayMs = 1000.0 / fps;
        streamTask = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                var frameStart = sw.Elapsed.TotalMilliseconds;

                try
                {
                    var frame = await CaptureFrameToMemoryAsync(token).ConfigureAwait(false);
                    if (frameCallback != null)
                        await frameCallback(frame).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Stream frame capture failed");
                }

                var elapsed = sw.Elapsed.TotalMilliseconds - frameStart;
                var delay = (int)Math.Max(1, delayMs - elapsed);
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        Plugin.Log.Info("Stream started at {Fps} FPS", fps);
    }

    public async Task StopStreamingAsync()
    {
        // Cancel first, then wait for the task to finish before disposing/nullifying.
        if (streamCts != null)
        {
            await streamCts.CancelAsync().ConfigureAwait(false);
        }

        if (streamTask != null)
        {
            try { await streamTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Plugin.Log.Error(ex, "Stream task finalization failed"); }
            streamTask = null;
        }

        if (streamCts != null)
        {
            streamCts.Dispose();
            streamCts = null;
        }

        frameCallback = null;
        isStreaming = false;
    }

    public void Dispose()
    {
        StopStreamingAsync().GetAwaiter().GetResult();
        captureLock.Dispose();
    }

    private async Task<byte[]> CaptureFrameToMemoryAsync(CancellationToken ct)
    {
        await captureLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var (origin, width, height) = GetCaptureRegion();
                using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

                using var ms = new System.IO.MemoryStream();
                SaveJpeg(bitmap, ms, configuration.ScreenshotJpegQuality);
                return ms.ToArray();
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            captureLock.Release();
        }
    }

    private ScreenshotReadyEvent CaptureToFile()
    {
        var (origin, width, height) = GetCaptureRegion();

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

    private static (NativePoint Origin, int Width, int Height) GetCaptureRegion()
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

        return (origin, width, height);
    }

    private static void SaveJpeg(Image image, string path, int quality)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        SaveJpeg(image, fs, quality);
    }

    private static void SaveJpeg(Image image, Stream stream, int quality)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.FormatID == ImageFormat.Jpeg.Guid);
        if (encoder == null)
        {
            image.Save(stream, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        image.Save(stream, encoder, parameters);
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
