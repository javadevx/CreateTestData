using System;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;

internal static class Program
{
    private const string BaseUrl = "http://localhost:8080";
    private const int NumThreads = 4;
    private const int MinRatePerSecond = 5;   // inclusive
    private const int MaxRatePerSecond = 100; // inclusive
    private const int MinPathValue = 1;       // inclusive
    private const int MaxPathValue = 10;      // inclusive
    private const int DefaultDurationSeconds = 10;

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static readonly ThreadLocal<Random> ThreadRandom = new ThreadLocal<Random>(() =>
        new Random(RandomNumberGenerator.GetInt32(0, int.MaxValue))
    );

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HttpLoadGen/1.0");
        return client;
    }

    private static void Log(string message)
    {
        var now = DateTime.UtcNow;
        var rounded = new DateTime(
            ((now.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond,
            DateTimeKind.Utc
        );
        string timestamp = rounded.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        Console.WriteLine($"[{timestamp}] {message}");
    }

    public static void Main(string[] args)
    {
        int durationSeconds = DefaultDurationSeconds;

        Log($"Starting {NumThreads} threads. Each runs for {durationSeconds}s.");

        var threadSummaries = new ThreadSummary[NumThreads];
        var threads = new Thread[NumThreads];

        for (int i = 0; i < NumThreads; i++)
        {
            int threadIndex = i;
            threads[i] = new Thread(() =>
            {
                threadSummaries[threadIndex] = RunWorkerThread(threadIndex, durationSeconds);
            })
            {
                IsBackground = false,
                Name = $"Worker-{i + 1}"
            };
        }

        var start = Stopwatch.StartNew();
        foreach (var t in threads)
        {
            t.Start();
        }
        foreach (var t in threads)
        {
            t.Join();
        }
        start.Stop();

        // Print summary
        long totalOk = 0;
        long totalFail = 0;
        long totalCycles = 0;
        for (int i = 0; i < NumThreads; i++)
        {
            var s = threadSummaries[i];
            totalOk += s.SuccessCount;
            totalFail += s.FailureCount;
            totalCycles += s.CyclesCompleted;
            Log($"Thread {i + 1}: path={s.PathValue} cycles={s.CyclesCompleted:N0} ok={s.SuccessCount:N0} fail={s.FailureCount:N0} rps={s.RatePerSecond}");
        }
        Log($"Total: cycles={totalCycles:N0} ok={totalOk:N0} fail={totalFail:N0} elapsed={start.Elapsed} (~{(totalOk + totalFail) / Math.Max(1, start.Elapsed.TotalSeconds):F1} req/s overall)");
    }

    private static ThreadSummary RunWorkerThread(int threadIndex, int durationSeconds)
    {
        var random = ThreadRandom.Value!;
        string threadName = Thread.CurrentThread.Name ?? $"Worker-{threadIndex + 1}";

        // Choose a fixed rate per thread within [5,100]
        int ratePerSecond = random.Next(MinRatePerSecond, MaxRatePerSecond + 1);
        double intervalSeconds = 1.0 / ratePerSecond;

        // Choose a fixed path value per thread within [1,10]
        int pathValue = random.Next(MinPathValue, MaxPathValue + 1);
        string url = $"{BaseUrl}/count/{pathValue}";

        long successCount = 0;
        long failureCount = 0;
        long executedCount = 0;

        var stopwatch = Stopwatch.StartNew();
        long lastReportSecond = 0;
        long ticksPerCall = (long)(TimeSpan.TicksPerSecond * intervalSeconds);
        long startTicks = stopwatch.ElapsedTicks;
        long callIndex = 0;
        

        while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
        {
            // Use the same URL for the entire thread lifetime
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = SharedHttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }
            catch
            {
                failureCount++;
            }

            executedCount++;
            callIndex++;

            // Once per second, print thread name and cumulative executions
            long elapsedSeconds = (long)stopwatch.Elapsed.TotalSeconds;
            if (elapsedSeconds > lastReportSecond)
            {
                double elapsedSecExact = stopwatch.Elapsed.TotalSeconds;
                double actualRps = elapsedSecExact > 0 ? executedCount / elapsedSecExact : 0.0;
                lastReportSecond = elapsedSeconds;
                Log($"{threadName} executed={executedCount} path={pathValue} rps={actualRps:F1}");
            }

            // Pace to achieve the target rate per second
            long dueTicks = startTicks + callIndex * ticksPerCall;
            long nowTicks = stopwatch.ElapsedTicks;
            long remainingTicks = dueTicks - nowTicks;
            if (remainingTicks > 0)
            {
                int sleepMillis = (int)(remainingTicks * 1000 / Stopwatch.Frequency);
                if (sleepMillis > 0)
                {
                    Thread.Sleep(sleepMillis);
                }
                // Optional short busy wait for sub-millisecond precision
                while (stopwatch.ElapsedTicks < dueTicks)
                {
                    // spin briefly
                }
            }
        }

        stopwatch.Stop();

        return new ThreadSummary
        {
            ThreadIndex = threadIndex,
            RatePerSecond = ratePerSecond,
            CyclesCompleted = executedCount,
            SuccessCount = successCount,
            FailureCount = failureCount,
            PathValue = pathValue
        };
    }

    private sealed class ThreadSummary
    {
        public int ThreadIndex { get; set; }
        public int RatePerSecond { get; set; }
        public long CyclesCompleted { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public int PathValue { get; set; }
    }
}
