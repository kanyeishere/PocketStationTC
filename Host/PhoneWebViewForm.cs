using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PocketStation.Host;

/// <summary>
/// A phone-sized WinForms window hosting a WebView2 browser,
/// used to display the Pocket Station web UI in-game.
/// </summary>
internal sealed class PhoneWebViewForm : Form
{
    private readonly WebView2 _webView = new();
    private bool _initialized;
    private bool _closing;

    /// <param name="url">The URL to navigate to.</param>
    /// <param name="userDataFolder">Directory for WebView2 user data.</param>
    public PhoneWebViewForm(string url, string userDataFolder)
    {
        // iPhone 14 logical dimensions
        ClientSize = new Size(390, 844);
        Text = "Pocket Station";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        // Try to set the window icon from icon.png next to the assembly
        try
        {
            var iconPath = Path.Combine(
                AppContext.BaseDirectory, "icon.png");
            if (File.Exists(iconPath))
            {
                using var bmp = new Bitmap(iconPath);
                var hIcon = bmp.GetHicon();
                Icon = Icon.FromHandle(hIcon);
            }
        }
        catch
        {
            // icon is optional
        }

        _webView.Dock = DockStyle.Fill;
        _webView.Visible = false;
        _webView.MouseEnter += (_, _) => MouseCapture.ReleaseGameCapture();
        _webView.GotFocus += (_, _) => MouseCapture.ReleaseGameCapture();
        Controls.Add(_webView);

        // Show a simple "loading" label while WebView2 initialises
        var label = new Label
        {
            Text = "正在加载…",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 14f),
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
        };
        Controls.Add(label);
        label.BringToFront();

        Load += async (_, _) => await InitializeAsync(url, userDataFolder);
        Shown += (_, _) => MouseCapture.ReleaseGameCapture();
        Activated += (_, _) => MouseCapture.ReleaseGameCapture();
        FormClosing += (_, _) =>
        {
            _closing = true;
            try { _webView.Dispose(); }
            catch { /* ignored */ }
        };
    }

    private async Task InitializeAsync(string url, string userDataFolder)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            // Hide the loading label and show the webview
            foreach (Control c in Controls)
            {
                if (c is Label) c.Visible = false;
            }
            _webView.Visible = true;
            _webView.BringToFront();
            _webView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            if (_closing || IsDisposed || Disposing)
                return;

            Plugin.Log.Error(ex, "Failed to initialize WebView2, falling back to default browser");

            // Fallback: open the URL in the system default browser
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // ignored
            }

            Close();
        }
    }
}

internal sealed class PhoneWebViewWindow : IDisposable
{
    private readonly ManualResetEventSlim initialized = new();
    private readonly Thread thread;
    private readonly string url;
    private readonly string userDataFolder;

    private WindowsFormsSynchronizationContext? context;
    private PhoneWebViewForm? form;
    private Exception? initializationException;
    private int isClosed;
    private int isDisposed;

    public PhoneWebViewWindow(string url, string userDataFolder)
    {
        this.url = url;
        this.userDataFolder = userDataFolder;

        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Pocket Station WebView2",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        initialized.Wait();

        if (initializationException is not null)
            throw new InvalidOperationException("Unable to initialize Pocket Station WebView2 window.", initializationException);
    }

    public bool IsClosed => Volatile.Read(ref isClosed) != 0;

    public void Activate() =>
        Post(() =>
        {
            if (form is not { IsDisposed: false } target)
                return;

            if (target.WindowState == FormWindowState.Minimized)
                target.WindowState = FormWindowState.Normal;

            MouseCapture.ReleaseGameCapture();
            target.Show();
            target.BringToFront();
            target.Activate();
        });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
            return;

        if (Thread.CurrentThread == thread)
        {
            form?.Close();
            Application.ExitThread();
            return;
        }

        Volatile.Read(ref context)?.Post(static state =>
        {
            var owner = (PhoneWebViewWindow)state!;
            owner.form?.Close();
            Application.ExitThread();
        }, this);

        thread.Join();
        initialized.Dispose();
    }

    private void Run()
    {
        try
        {
            using var synchronizationContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            context = synchronizationContext;

            form = new PhoneWebViewForm(url, userDataFolder);
            form.FormClosed += (_, _) => Volatile.Write(ref isClosed, 1);

            initialized.Set();
            Application.Run(form);
        }
        catch (Exception ex)
        {
            initializationException = ex;
            Volatile.Write(ref isClosed, 1);
            initialized.Set();
        }
        finally
        {
            try { form?.Dispose(); }
            catch { /* ignored */ }

            form = null;
            SynchronizationContext.SetSynchronizationContext(null);
            Volatile.Write(ref isClosed, 1);
        }
    }

    private void Post(Action action)
    {
        if (Volatile.Read(ref isDisposed) != 0 || IsClosed)
            return;

        Volatile.Read(ref context)?.Post(static state =>
        {
            var (owner, callback) = ((PhoneWebViewWindow Owner, Action Callback))state!;
            if (Volatile.Read(ref owner.isDisposed) != 0 || owner.IsClosed)
                return;

            callback();
        }, (this, action));
    }
}

internal static class MouseCapture
{
    public static void ReleaseGameCapture()
    {
        try
        {
            ReleaseCapture();
            ClipCursor(nint.Zero);
        }
        catch
        {
            // Best-effort: the WebView should still work even if Windows rejects this.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(nint lpRect);
}
