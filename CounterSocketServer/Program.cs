using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

public static class Program
{
    private static readonly ConcurrentDictionary<int, int> PerKeyCounters = new();
    private static int GlobalCounter = 0;

    public static async Task Main(string[] args)
    {
        int port = 8080;
        if (args.Length > 0 && int.TryParse(args[0], out var parsed))
        {
            port = parsed;
        }

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Listening on 0.0.0.0:{port}");

        var acceptTasks = new List<Task>();
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        // using (client)
        using var networkStream = client.GetStream();
        using var reader = new StreamReader(networkStream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(networkStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        string? requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        // Read and discard headers until blank line
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }

        string method, path, httpVersion;
        var parts = requestLine.Split(' ');
        if (parts.Length >= 3)
        {
            method = parts[0];
            path = parts[1];
            httpVersion = parts[2];
        }
        else
        {
            await WriteResponse(writer, 400, new { error = "Bad Request" });
            return;
        }

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponse(writer, 405, new { error = "Method Not Allowed" });
            return;
        }

        if (TryMatch(path, "/count/", out var idStr) && int.TryParse(idStr, out var id))
        {
            var newPerId = PerKeyCounters.AddOrUpdate(id, 1, (_, current) => checked(current + 1));
            var newGlobal = Interlocked.Increment(ref GlobalCounter);
            await WriteResponse(writer, 200, new { id, perIdCount = newPerId, globalCount = newGlobal });
            return;
        }

        if (TryMatch(path, "/peek/", out idStr) && int.TryParse(idStr, out id))
        {
            PerKeyCounters.TryGetValue(id, out var currentPerId);
            var currentGlobal = Volatile.Read(ref GlobalCounter);
            await WriteResponse(writer, 200, new { id, perIdCount = currentPerId, globalCount = currentGlobal });
            return;
        }

        if (path == "/")
        {
            await WritePlain(writer, 200, "Use GET /count/{id} to increment per-id and global counters\nUse GET /peek/{id} to read without incrementing\n");
            return;
        }

        await WriteResponse(writer, 404, new { error = "Not Found" });
    }

    private static bool TryMatch(string path, string prefix, out string idPart)
    {
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            idPart = path.Substring(prefix.Length);
            var qIndex = idPart.IndexOf('?');
            if (qIndex >= 0)
            {
                idPart = idPart.Substring(0, qIndex);
            }
            return idPart.Length > 0;
        }
        idPart = string.Empty;
        return false;
    }

    private static async Task WriteResponse(StreamWriter writer, int statusCode, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        await WriteHttp(writer, statusCode, "application/json; charset=utf-8", bodyBytes);
    }

    private static async Task WritePlain(StreamWriter writer, int statusCode, string text)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(text);
        await WriteHttp(writer, statusCode, "text/plain; charset=utf-8", bodyBytes);
    }

    private static async Task WriteHttp(StreamWriter writer, int statusCode, string contentType, byte[] body)
    {
        string reason = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "OK"
        };

        await writer.WriteLineAsync($"HTTP/1.1 {statusCode} {reason}");
        await writer.WriteLineAsync($"Date: {DateTime.UtcNow:R}");
        await writer.WriteLineAsync("Connection: close");
        await writer.WriteLineAsync($"Content-Type: {contentType}");
        await writer.WriteLineAsync($"Content-Length: {body.Length}");
        await writer.WriteLineAsync();
        await writer.FlushAsync();
        await writer.BaseStream.WriteAsync(body, 0, body.Length);
        await writer.BaseStream.FlushAsync();
    }
}
