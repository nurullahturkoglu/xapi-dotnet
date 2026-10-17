using BenchmarkDotNet.Attributes;
using Xapi.Client;
using Xapi.Client.Outbox;
using Xapi.Json;

namespace Xapi.Benchmarks.Outbox;

// StatementOutbox.EnqueueAsync of one statement: id assignment, validation and the store write. FileOutboxStore
// flushes its journal to disk (fsync) before every enqueue returns, so its numbers are mostly the disk's flush
// latency. Nothing is sent: the outbox is never flushed and its background loop is not started.
[MemoryDiagnoser]
public class EnqueueBenchmarks
{
    private string _directory = "";
    private HttpClient _http = null!;
    private IOutboxStore _store = null!;
    private StatementOutbox _outbox = null!;
    private Statement _statement = null!;

    [Params("InMemory", "File")]
    public string Store { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        // The id is left out so EnqueueAsync assigns one, as it does for application code
        _statement = XapiJson.ParseStatement(StatementSamples.Statement(id: null, 7), XapiVersion.V2_0_0).GetValueOrThrow();

        _directory = Path.Combine(Path.GetTempPath(), "xapi-bench-outbox-" + Guid.NewGuid().ToString("N"));
        _store = Store == "File" ? new FileOutboxStore(_directory) : new InMemoryOutboxStore();
        _http = new HttpClient();
        var client = new XapiClient(_http, new XapiClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:9/xapi/"),
            Version = XapiVersion.V2_0_0,
        });
        // No queue limit: the benchmark enqueues far more than the default 100,000 and never drains
        _outbox = new StatementOutbox(client, _store, new OutboxOptions { MaxQueueSize = null });
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _outbox.DisposeAsync();
        (_store as IDisposable)?.Dispose();
        _http.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Benchmark]
    public ValueTask<Guid> EnqueueAsync() => _outbox.EnqueueAsync(_statement);
}
