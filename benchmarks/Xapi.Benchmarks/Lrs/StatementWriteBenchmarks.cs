using BenchmarkDotNet.Attributes;

namespace Xapi.Benchmarks.Lrs;

// POST /xapi/statements with one new statement or a batch of 100 new statements, from one writer. Every
// operation stores fresh ids, so the database grows over the run (to tens of thousands of statements). Building
// the request JSON is part of the measured time; it costs a few microseconds per statement.
[MemoryDiagnoser]
public class StatementWriteBenchmarks
{
    private LrsServer _server = null!;
    private int _next;

    [ParamsSource(typeof(LrsDatabases), nameof(LrsDatabases.All))]
    public string Database { get; set; } = "";

    [GlobalSetup]
    public async Task SetupAsync() => _server = await LrsServer.StartAsync(Database);

    [GlobalCleanup]
    public async Task CleanupAsync() => await _server.DisposeAsync();

    [Benchmark(Description = "POST 1 statement")]
    public Task PostOne() => _server.PostAsync(StatementSamples.Statement(Guid.NewGuid(), _next++));

    [Benchmark(Description = "POST batch of 100")]
    public Task PostBatch()
    {
        var start = _next;
        _next += 100;
        return _server.PostAsync(StatementSamples.Batch(100, start));
    }
}
