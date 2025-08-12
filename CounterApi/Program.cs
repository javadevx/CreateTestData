var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Use a shared, thread-safe counter store
var globalCounter = 0;
var perKeyCounters = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

// Root info
app.MapGet("/", () => "Use GET /count/{id} to increment per-id and global counters");

// Increment and return counters for a given id, along with global
app.MapGet("/count/{id:int}", (int id) =>
{
    var newPerId = perKeyCounters.AddOrUpdate(id, 1, (_, current) => checked(current + 1));
    var newGlobal = System.Threading.Interlocked.Increment(ref globalCounter);

    return Results.Json(new { id, perIdCount = newPerId, globalCount = newGlobal });
});

// Optional endpoint to read current values without incrementing
app.MapGet("/peek/{id:int}", (int id) =>
{
    perKeyCounters.TryGetValue(id, out var currentPerId);
    var currentGlobal = System.Threading.Volatile.Read(ref globalCounter);
    return Results.Json(new { id, perIdCount = currentPerId, globalCount = currentGlobal });
});

app.Run();
