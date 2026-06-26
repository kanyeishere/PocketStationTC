using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PocketStation.Infrastructure.Network;

/// <summary>
/// Parsed HTTP request. Extracted from LanWebServer to be shared with controllers.
/// </summary>
public sealed record HttpRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);

/// <summary>
/// Static HTTP response helpers shared by LanWebServer and controllers.
/// </summary>
public static class HttpHelpers
{
    public static async Task WriteJsonAsync(NetworkStream stream, object value, CancellationToken ct)
    {
        await WriteResponseAsync(stream, 200, "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(value, Plugin.JsonOptions), ct).ConfigureAwait(false);
    }

    public static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string contentType,
        byte[] body,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            503 => "Service Unavailable",
            _ => "OK"
        };

        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        builder.Append("Connection: close\r\n");
        builder.Append("Access-Control-Allow-Origin: *\r\n");
        builder.Append("Access-Control-Allow-Headers: Content-Type, X-Pocket-Token, Authorization\r\n");
        if (extraHeaders != null)
        {
            foreach (var (key, value) in extraHeaders)
                builder.Append(key).Append(": ").Append(value).Append("\r\n");
        }

        builder.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), ct).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    public static byte[] JsonBytes(object value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Plugin.JsonOptions);

    public static bool IsAuthorized(HttpRequest request, string? requireToken, string? expectedToken)
    {
        if (string.IsNullOrEmpty(requireToken) || string.IsNullOrEmpty(expectedToken))
            return true;

        if (request.Query.TryGetValue("token", out var queryToken) &&
            ConstantTimeEquals(queryToken, expectedToken))
            return true;

        if (request.Headers.TryGetValue("x-pocket-token", out var headerToken) &&
            ConstantTimeEquals(headerToken, expectedToken))
            return true;

        if (request.Headers.TryGetValue("authorization", out var authorization) &&
            authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            ConstantTimeEquals(authorization[7..].Trim(), expectedToken))
            return true;

        return false;
    }

    public static string GetAccessUrl(int port, string token)
    {
        foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                return $"http://{address}:{port}/?token={Uri.EscapeDataString(token)}";
        }

        return $"http://127.0.0.1:{port}/?token={Uri.EscapeDataString(token)}";
    }

    public static IReadOnlyList<string> GetAccessUrls(int port, string token)
    {
        var urls = new List<string>();
        foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                urls.Add($"http://{address}:{port}/?token={Uri.EscapeDataString(token)}");
        }

        if (urls.Count == 0)
            urls.Add($"http://127.0.0.1:{port}/?token={Uri.EscapeDataString(token)}");

        return urls;
    }

    public static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private static bool ConstantTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
