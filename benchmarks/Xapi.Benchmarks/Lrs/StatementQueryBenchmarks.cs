using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Xapi.Benchmarks.Lrs;

// GET /xapi/statements against 10,000 or 100,000 stored statements spread over 10 verbs and 500 learners: one
// statement by id, the first page of a verb filter (limit 100, so a tenth of the statements match and there is a
// "more" link), the page that link returns, a verb filter that matches nothing, and a learner with
// related_agents (one statement in 500). The last two are where a query that walks the statements newest first
// and tests each one would read most of the table. With StatementRefPercent = 10, every tenth statement is an
// instructor's comment whose object is a StatementRef to the statement before it, so the matches through
// StatementRefs have 1,000 or 10,000 rows to look at; with 1, every hundredth, so 100 or 1,000 rows. None of them
// match these filters, and the other statements and their matches are the same as without them, so the
// difference to StatementRefPercent = 0 is the cost of resolving StatementRefs when the query runs.
[MemoryDiagnoser]
public class StatementQueryBenchmarks
{
    private static readonly Uri ByVerb = new($"statements?verb={Uri.EscapeDataString(StatementSamples.Verbs[1])}&limit=100", UriKind.Relative);

    private static readonly Uri ByUnusedVerb = new($"statements?verb={Uri.EscapeDataString("http://example.com/verbs/unused")}&limit=100", UriKind.Relative);

    private static readonly Uri ByRelatedAgent = new($"statements?agent={Uri.EscapeDataString("""{"mbox":"mailto:learner7@example.com"}""")}&related_agents=true&limit=100", UriKind.Relative);

    private LrsServer _server = null!;
    private Uri _byId = null!;
    private Uri _more = null!;

    [ParamsSource(typeof(LrsDatabases), nameof(LrsDatabases.All))]
    public string Database { get; set; } = "";

    [Params(10_000, 100_000)]
    public int Stored { get; set; }

    [Params(0, 1, 10)]
    public int StatementRefPercent { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _server = await LrsServer.StartAsync(Database);
        for (var start = 0; start < Stored; start += 100)
        {
            await _server.PostAsync(StatementSamples.Batch(100, start, StatementRefPercent == 0 ? 0 : 100 / StatementRefPercent));
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

    [Benchmark(Description = "GET agent filter, related_agents")]
    public Task<int> GetByRelatedAgent() => GetAsync(ByRelatedAgent);

    private async Task<int> GetAsync(Uri uri)
    {
        using var response = await _server.Client.GetAsync(uri);
        await LrsServer.EnsureSuccessAsync(response);
        var body = await response.Content.ReadAsByteArrayAsync();
        return body.Length;
    }
}
