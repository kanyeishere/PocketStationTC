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
    private readonly SemaphoreSlim streamStateLock = new(1, 1);
    private static readonly TimeSpan StreamStopTimeout = TimeSpan.FromSeconds(3);

    private string? latestPath;
    private int latestWidth;
    private int latestHeight;

    private CancellationTokenSource? streamCts;
    private Task? streamTask;
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

    public async Task StartStreamingAsync(int fps, Func<byte[], CancellationToken, Task> onFrame)
    {
        await streamStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopStreamingCoreAsync().ConfigureAwait(false);

            fps = Math.Clamp(fps, 1, 120);
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
                        await onFrame(frame, token).ConfigureAwait(false);
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
        finally
        {
            streamStateLock.Release();
        }
    }

    public async Task StopStreamingAsync()
    {
        await streamStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopStreamingCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            streamStateLock.Release();
        }
    }

    private async Task StopStreamingCoreAsync()
    {
        var cts = streamCts;
        var task = streamTask;

        if (cts != null)
            await cts.CancelAsync().ConfigureAwait(false);

        if (task != null)
        {
            try { await task.WaitAsync(StreamStopTimeout).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (TimeoutException ex)
            {
                Plugin.Log.Warning(ex, "Stream task did not stop within {TimeoutMs}ms; detaching it.",
                    (int)StreamStopTimeout.TotalMilliseconds);
            }
            catch (Exception ex) { Plugin.Log.Error(ex, "Stream task finalization failed"); }
        }

        streamTask = null;
        streamCts = null;
        isStreaming = false;

        if (cts == null)
            return;

        if (task == null || task.IsCompleted)
        {
            cts.Dispose();
            return;
        }

        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                _ = t.Exception;

            cts.Dispose();
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        StopStreamingAsync().GetAwaiter().GetResult();
        captureLock.Dispose();
        streamStateLock.Dispose();
    }

    private async Task<byte[]> CaptureFrameToMemoryAsync(CancellationToken ct)
    {
        await captureLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var bitmap = CaptureGameWindow();
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
        var tempPath = Path.Combine(cacheDirectory, "latest.tmp.jpg");
        var finalPath = Path.Combine(cacheDirectory, "latest.jpg");

        using var bitmap = CaptureGameWindow();
        var width = bitmap.Width;
        var height = bitmap.Height;
        SaveJpeg(bitmap, tempPath, configuration.ScreenshotJpegQuality);

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

    private static Bitmap CaptureGameWindow()
    {
        var region = GetCaptureRegion();
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);

        try
        {
            if (TryCaptureWindowClient(region.Hwnd, bitmap))
                return bitmap;

            if (TryCaptureWindowDeviceContext(region, bitmap))
                return bitmap;

            throw new InvalidOperationException("Failed to capture game window content.");
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static bool TryCaptureWindowClient(IntPtr hwnd, Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        try
        {
            var flags = PrintWindowFlags.ClientOnly | PrintWindowFlags.RenderFullContent;
            if (!PrintWindow(hwnd, hdc, flags))
            {
                Plugin.Log.Debug("PrintWindow failed; falling back to game window DC capture. LastError={LastError}",
                    Marshal.GetLastWin32Error());
                return false;
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        if (!IsLikelyBlank(bitmap))
            return true;

        Plugin.Log.Debug("PrintWindow returned a blank frame; falling back to game window DC capture.");
        return false;
    }

    private static bool TryCaptureWindowDeviceContext(CaptureRegion region, Bitmap bitmap)
    {
        var sourceDc = GetDC(region.Hwnd);
        if (sourceDc == IntPtr.Zero)
        {
            Plugin.Log.Debug("GetDC failed while capturing game window. LastError={LastError}",
                Marshal.GetLastWin32Error());
            return false;
        }

        using var graphics = Graphics.FromImage(bitmap);
        var targetDc = graphics.GetHdc();

        try
        {
            if (BitBlt(targetDc, 0, 0, region.Width, region.Height, sourceDc, 0, 0, RasterOperation.SourceCopy))
                return true;

            Plugin.Log.Debug("BitBlt failed while capturing game window. LastError={LastError}",
                Marshal.GetLastWin32Error());
            return false;
        }
        finally
        {
            graphics.ReleaseHdc(targetDc);
            ReleaseDC(region.Hwnd, sourceDc);
        }
    }

    private static bool IsLikelyBlank(Bitmap bitmap)
    {
        const int tolerance = 2;
        var first = bitmap.GetPixel(0, 0);
        var stepX = Math.Max(1, bitmap.Width / 8);
        var stepY = Math.Max(1, bitmap.Height / 8);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - first.R) > tolerance ||
                    Math.Abs(pixel.G - first.G) > tolerance ||
                    Math.Abs(pixel.B - first.B) > tolerance)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static CaptureRegion GetCaptureRegion()
    {
        var hwnd = Plugin.PluginInterface.UiBuilder.WindowHandlePtr;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Game window handle is unavailable.");

        if (!GetClientRect(hwnd, out var rect))
            throw new InvalidOperationException("Failed to read game client rectangle.");

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        return new CaptureRegion(hwnd, width, height);
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
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, PrintWindowFlags nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hdc,
        int x,
        int y,
        int cx,
        int cy,
        IntPtr hdcSrc,
        int x1,
        int y1,
        RasterOperation rop);

    private readonly record struct CaptureRegion(IntPtr Hwnd, int Width, int Height);

    [Flags]
    private enum PrintWindowFlags : uint
    {
        ClientOnly = 0x00000001,
        RenderFullContent = 0x00000002
    }

    private enum RasterOperation : uint
    {
        SourceCopy = 0x00CC0020
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

}
