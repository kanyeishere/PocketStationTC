using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PocketStation.Host;

namespace PocketStation.Infrastructure.Telemetry;

internal static class PocketBackendClient
{
    private const string BackendBaseUrl = "http://203.132.80.202:9898";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void QueueHeartbeat(Configuration configuration, string eventName, object? metadata = null)
    {
        if (!CanSend(configuration))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await PostJsonAsync(configuration, "/api/heartbeat", new
                {
                    app = "station",
                    installId = configuration.InstallId,
                    version = GetVersion(),
                    @event = eventName,
                    metadata,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SuppressTelemetryFailure(ex);
            }
        });
    }

    private static bool CanSend(Configuration configuration)
        => configuration.EnablePocketBackendTelemetry &&
           !string.IsNullOrWhiteSpace(configuration.InstallId);

    private static async Task PostJsonAsync(Configuration configuration, string path, object payload)
    {
        Uri endpoint = BuildEndpoint(path);
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await HttpClient.PostAsync(endpoint, content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static Uri BuildEndpoint(string path)
    {
        string normalizedBase = BackendBaseUrl.EndsWith("/", StringComparison.Ordinal) ? BackendBaseUrl : BackendBaseUrl + "/";
        return new Uri(new Uri(normalizedBase), path.TrimStart('/'));
    }

    private static string GetVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    private static void SuppressTelemetryFailure(Exception _)
    {
        // Telemetry is best-effort and must not leave traces in plugin logs.
    }
}
