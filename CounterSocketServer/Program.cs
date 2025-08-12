using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;

public static class Program
{
    private static readonly ConcurrentDictionary<int, int> PerKeyCounters = new();
    private static int GlobalCounter = 0;

    // Performance metrics
    private static class Metrics
    {
        public static long TotalRequests;
        public static long TotalBytesIn;
        public static long TotalBytesOut;
        public static long TotalLatencyTicks;
        public static long MinLatencyTicks = long.MaxValue;
        public static long MaxLatencyTicks = 0;

        public static long CountRequests;
        public static long PeekRequests;
        public static long RootRequests;
        public static long OtherRequests;

        public static void RecordRequest(long bytesIn, long bytesOut, long latencyTicks, string route)
        {
            Interlocked.Increment(ref TotalRequests);
            Interlocked.Add(ref TotalBytesIn, bytesIn);
            Interlocked.Add(ref TotalBytesOut, bytesOut);
            Interlocked.Add(ref TotalLatencyTicks, latencyTicks);

            // Min
            long currentMin;
            while (true)
            {
                currentMin = Volatile.Read(ref MinLatencyTicks);
                if (latencyTicks >= currentMin) break;
                if (Interlocked.CompareExchange(ref MinLatencyTicks, latencyTicks, currentMin) == currentMin) break;
            }
            // Max
            long currentMax;
            while (true)
            {
                currentMax = Volatile.Read(ref MaxLatencyTicks);
                if (latencyTicks <= currentMax) break;
                if (Interlocked.CompareExchange(ref MaxLatencyTicks, latencyTicks, currentMax) == currentMax) break;
            }

            switch (route)
            {
                case "count": Interlocked.Increment(ref CountRequests); break;
                case "peek": Interlocked.Increment(ref PeekRequests); break;
                case "root": Interlocked.Increment(ref RootRequests); break;
                default: Interlocked.Increment(ref OtherRequests); break;
            }
        }

        public static object Snapshot()
        {
            var total = Volatile.Read(ref TotalRequests);
            var totalIn = Volatile.Read(ref TotalBytesIn);
            var totalOut = Volatile.Read(ref TotalBytesOut);
            var totalTicks = Volatile.Read(ref TotalLatencyTicks);
            var minTicks = Volatile.Read(ref MinLatencyTicks);
            var maxTicks = Volatile.Read(ref MaxLatencyTicks);

            double avgMs = total > 0 ? (totalTicks / (double)total) / TimeSpan.TicksPerMillisecond : 0;
            double minMs = (minTicks == long.MaxValue || total == 0) ? 0 : minTicks / (double)TimeSpan.TicksPerMillisecond;
            double maxMs = total == 0 ? 0 : maxTicks / (double)TimeSpan.TicksPerMillisecond;

            return new
            {
                totalRequests = total,
                totalBytesIn = totalIn,
                totalBytesOut = totalOut,
                avgLatencyMs = avgMs,
                minLatencyMs = minMs,
                maxLatencyMs = maxMs,
                perRoute = new
                {
                    count = Volatile.Read(ref CountRequests),
                    peek = Volatile.Read(ref PeekRequests),
                    root = Volatile.Read(ref RootRequests),
                    other = Volatile.Read(ref OtherRequests)
                }
            };
        }
    }

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
        var stopwatch = Stopwatch.StartNew();
        long bytesIn = 0;
        long bytesOut = 0;
        string route = "other";

        try
        {
            using (client)
            using var networkStream = client.GetStream();
            using var reader = new StreamReader(networkStream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(networkStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

            string? requestLine = await reader.ReadLineAsync();
            if (requestLine != null)
            {
                bytesIn += Encoding.ASCII.GetByteCount(requestLine) + 2; // include CRLF
            }
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                bytesOut = await WriteResponse(writer, 400, new { error = "Bad Request" });
                route = "other";
                return;
            }

            // Read and discard headers until blank line
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                bytesIn += Encoding.ASCII.GetByteCount(line) + 2; // include CRLF
            }
            // final blank line CRLF
            bytesIn += 2;

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
                bytesOut = await WriteResponse(writer, 400, new { error = "Bad Request" });
                route = "other";
                return;
            }

            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                bytesOut = await WriteResponse(writer, 405, new { error = "Method Not Allowed" });
                route = "other";
                return;
            }

            if (TryMatch(path, "/count/", out var idStr) && int.TryParse(idStr, out var id))
            {
                var newPerId = PerKeyCounters.AddOrUpdate(id, 1, (_, current) => checked(current + 1));
                var newGlobal = Interlocked.Increment(ref GlobalCounter);
                bytesOut = await WriteResponse(writer, 200, new { id, perIdCount = newPerId, globalCount = newGlobal });
                route = "count";
                return;
            }

            if (TryMatch(path, "/peek/", out idStr) && int.TryParse(idStr, out id))
            {
                PerKeyCounters.TryGetValue(id, out var currentPerId);
                var currentGlobal = Volatile.Read(ref GlobalCounter);
                bytesOut = await WriteResponse(writer, 200, new { id, perIdCount = currentPerId, globalCount = currentGlobal });
                route = "peek";
                return;
            }

            if (path == "/metrics")
            {
                var snapshot = Metrics.Snapshot();
                bytesOut = await WriteResponse(writer, 200, snapshot);
                route = "other";
                return;
            }

            if (path == "/")
            {
                bytesOut = await WritePlain(writer, 200, "Use GET /count/{id} to increment per-id and global counters\nUse GET /peek/{id} to read without incrementing\nUse GET /metrics to retrieve performance counters\n");
                route = "root";
                return;
            }

            bytesOut = await WriteResponse(writer, 404, new { error = "Not Found" });
            route = "other";
        }
        finally
        {
            stopwatch.Stop();
            Metrics.RecordRequest(bytesIn, bytesOut, stopwatch.ElapsedTicks, route);
        }
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

    private static async Task<long> WriteResponse(StreamWriter writer, int statusCode, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        return await WriteHttp(writer, statusCode, "application/json; charset=utf-8", bodyBytes);
    }

    private static async Task<long> WritePlain(StreamWriter writer, int statusCode, string text)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(text);
        return await WriteHttp(writer, statusCode, "text/plain; charset=utf-8", bodyBytes);
    }

    private static async Task<long> WriteHttp(StreamWriter writer, int statusCode, string contentType, byte[] body)
    {
        string reason = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "OK"
        };

        var headers = new StringBuilder();
        headers.Append($"HTTP/1.1 {statusCode} {reason}\r\n");
        headers.Append($"Date: {DateTime.UtcNow:R}\r\n");
        headers.Append("Connection: close\r\n");
        headers.Append($"Content-Type: {contentType}\r\n");
        headers.Append($"Content-Length: {body.Length}\r\n\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(headers.ToString());
        await writer.BaseStream.WriteAsync(headerBytes, 0, headerBytes.Length);
        await writer.BaseStream.WriteAsync(body, 0, body.Length);
        await writer.BaseStream.FlushAsync();

        return headerBytes.Length + body.Length;
    }
}