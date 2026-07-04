using System.Diagnostics;
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
        FormClosing += (_, _) =>
        {
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
