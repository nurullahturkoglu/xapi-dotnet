using BenchmarkDotNet.Attributes;

namespace Xapi.Benchmarks.Lrs;

// 512 single-statement POSTs per operation, made by 1, 4 or 8 writers in parallel. The statements have distinct
// ids but, like real course traffic, share activities (40 lessons, one course, one category) and 500 learners,
// so concurrent transactions read and write the same activity and agent rows. The time is per statement.
// Retries of the statement store (a serialization failure, deadlock or unique key violation on PostgreSQL,
// SQLITE_BUSY on SQLite) are counted and printed after the run. An operation ends when its slowest writer ends,
// and a retry waits 10 to 30 ms first, so with few statements per writer the tail dominates; 512 keeps it small.
[MemoryDiagnoser]
public class ConcurrentWriterBenchmarks
{
    private const int StatementsPerOperation = 512;

    private LrsServer _server = null!;
    private int _next;

    [ParamsSource(typeof(LrsDatabases), nameof(LrsDatabases.All))]
    public string Database { get; set; } = "";

    [Params(1, 4, 8)]
    public int Writers { get; set; }

    [GlobalSetup]
    public async Task SetupAsync() => _server = await LrsServer.StartAsync(Database);

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        BenchmarkCounters.Record($"{Database}, {Writers} writer(s)", _next, _server.Retries.Retries);
        await _server.DisposeAsync();
    }

    [Benchmark(Description = "POST 1 statement, concurrent", OperationsPerInvoke = StatementsPerOperation)]
    public Task PostConcurrently()
    {
        var perWriter = StatementsPerOperation / Writers;
        var writers = new Task[Writers];
        for (var w = 0; w < Writers; w++)
        {
            var first = Interlocked.Add(ref _next, perWriter) - perWriter;
            writers[w] = Task.Run(async () =>
            {
                for (var i = 0; i < perWriter; i++)
                {
                    await _server.PostAsync(StatementSamples.Statement(Guid.NewGuid(), first + i));
                }
            });
        }

        return Task.WhenAll(writers);
    }
}
