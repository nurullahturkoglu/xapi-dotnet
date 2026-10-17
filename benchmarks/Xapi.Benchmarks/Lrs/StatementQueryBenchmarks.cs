using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Xapi.Benchmarks.Lrs;

// GET /xapi/statements against 10,000 stored statements spread over 10 verbs: one statement by id, the first
// page of a verb filter (limit 100, so 1,000 matches give a "more" link), the page that link returns, and a verb
// filter that matches nothing, which is the worst case for a query that walks the statements newest first.
[MemoryDiagnoser]
public class StatementQueryBenchmarks
{
    private const int Stored = 10_000;

    private static readonly Uri ByVerb = new($"statements?verb={Uri.EscapeDataString(StatementSamples.Verbs[1])}&limit=100", UriKind.Relative);

    private static readonly Uri ByUnusedVerb = new($"statements?verb={Uri.EscapeDataString("http://example.com/verbs/unused")}&limit=100", UriKind.Relative);

    private LrsServer _server = null!;
    private Uri _byId = null!;
    private Uri _more = null!;

    [ParamsSource(typeof(LrsDatabases), nameof(LrsDatabases.All))]
    public string Database { get; set; } = "";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _server = await LrsServer.StartAsync(Database);
        for (var start = 0; start < Stored; start += 100)
        {
            await _server.PostAsync(StatementSamples.Batch(100, start));
        }

        var id = Guid.NewGuid();
        await _server.PostAsync(StatementSamples.Statement(id, Stored));
        _byId = new Uri($"statements?statementId={id}", UriKind.Relative);

        using var response = await _server.Client.GetAsync(ByVerb);
        await LrsServer.EnsureSuccessAsync(response);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var more = page.RootElement.GetProperty("more").GetString();
        if (string.IsNullOrEmpty(more) || page.RootElement.GetProperty("statements").GetArrayLength() != 100)
        {
            throw new InvalidOperationException("Expected a full first page with a more link.");
        }

        // The more link is absolute from the server root (/xapi/statements?more=...)
        _more = new Uri(more, UriKind.Relative);
    }

    [GlobalCleanup]
    public async Task CleanupAsync() => await _server.DisposeAsync();

    [Benchmark(Description = "GET by statementId")]
    public Task<int> GetById() => GetAsync(_byId);

    [Benchmark(Description = "GET verb filter, limit 100")]
    public Task<int> GetByVerb() => GetAsync(ByVerb);

    [Benchmark(Description = "GET more page")]
    public Task<int> GetMore() => GetAsync(_more);

    [Benchmark(Description = "GET verb filter, no match")]
    public Task<int> GetByUnusedVerb() => GetAsync(ByUnusedVerb);

    private async Task<int> GetAsync(Uri uri)
    {
        using var response = await _server.Client.GetAsync(uri);
        await LrsServer.EnsureSuccessAsync(response);
        var body = await response.Content.ReadAsByteArrayAsync();
        return body.Length;
    }
}
