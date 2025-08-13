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
    private const int DefaultCyclesPerThread = 1_000_000;

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

    public static void Main(string[] args)
    {
        int cyclesPerThread = GetCyclesFromEnvOrDefault();

        Console.WriteLine($"Starting {NumThreads} threads. Each runs {cyclesPerThread:N0} cycles.");

        var threadSummaries = new ThreadSummary[NumThreads];
        var threads = new Thread[NumThreads];

        for (int i = 0; i < NumThreads; i++)
        {
            int threadIndex = i;
            threads[i] = new Thread(() =>
            {
                threadSummaries[threadIndex] = RunWorkerThread(threadIndex, cyclesPerThread);
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
            Console.WriteLine($"Thread {i + 1}: cycles={s.CyclesCompleted:N0} ok={s.SuccessCount:N0} fail={s.FailureCount:N0} rps={s.RatePerSecond}");
        }
        Console.WriteLine($"Total: cycles={totalCycles:N0} ok={totalOk:N0} fail={totalFail:N0} elapsed={start.Elapsed} (~{(totalOk + totalFail) / Math.Max(1, start.Elapsed.TotalSeconds):F1} req/s overall)");
    }

    private static int GetCyclesFromEnvOrDefault()
    {
        string? env = Environment.GetEnvironmentVariable("CYCLES");
        if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out int parsed) && parsed > 0)
        {
            return parsed;
        }
        return DefaultCyclesPerThread;
    }

    private static ThreadSummary RunWorkerThread(int threadIndex, int cyclesPerThread)
    {
        var random = ThreadRandom.Value!;

        // Choose a fixed rate per thread within [5,100]
        int ratePerSecond = random.Next(MinRatePerSecond, MaxRatePerSecond + 1);
        double intervalSeconds = 1.0 / ratePerSecond;

        long successCount = 0;
        long failureCount = 0;

        var stopwatch = Stopwatch.StartNew();
        long ticksPerCall = (long)(TimeSpan.TicksPerSecond * intervalSeconds);
        long startTicks = stopwatch.ElapsedTicks;

        for (int i = 0; i < cyclesPerThread; i++)
        {
            int pathValue = random.Next(MinPathValue, MaxPathValue + 1);
            string url = $"{BaseUrl}/count/{pathValue}";

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

            // Pace to achieve the target rate per second
            long dueTicks = startTicks + (i + 1) * ticksPerCall;
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
            CyclesCompleted = cyclesPerThread,
            SuccessCount = successCount,
            FailureCount = failureCount
        };
    }

    private sealed class ThreadSummary
    {
        public int ThreadIndex { get; set; }
        public int RatePerSecond { get; set; }
        public long CyclesCompleted { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
    }
}
