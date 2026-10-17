using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

public class ConcurrencyTests
{
    public static TheoryData<string> Stores => ["memory", "file"];

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Statements_enqueued_during_a_flush_are_all_delivered_once_and_in_order_per_producer(string kind)
    {
        using var directory = new TempDirectory();
        using var file = kind == "file" ? await FileOutboxStore.OpenAsync(directory.Path, Ct) : null;
        await using var h = Harness.Create(o => o.BatchSize = 7, store: (OutboxStore?)file ?? new InMemoryOutboxStore());
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        h.Lrs.BeforeHandle = async _ =>
        {
            if (first)
            {
                first = false;
                await release.Task;
            }
        };
        await h.EnqueueAsync(3);

        var flush = h.Outbox.FlushAsync(Ct);
        await Eventually(() => h.Lrs.Snapshot(() => h.Lrs.Requests.Count) == 1, "the first request is in flight");
        const int Producers = 4;
        const int PerProducer = 50;
        var producers = Enumerable.Range(0, Producers).Select(p => Task.Run(async () =>
        {
            var ids = new List<Guid>();
            for (var i = 0; i < PerProducer; i++)
            {
                ids.Add(await h.Outbox.EnqueueAsync(Statement(1000 + (p * PerProducer) + i), Ct));
                if (i == PerProducer / 2)
                {
                    release.TrySetResult(true);
                }
            }

            return ids;
        })).ToList();
        var produced = await Task.WhenAll(producers);
        await flush;
        await h.Outbox.FlushAsync(Ct);

        var stored = h.Lrs.StoredIds;
        Assert.Equal(3 + (Producers * PerProducer), stored.Count);
        Assert.Equal(stored.Count, stored.Distinct().Count());
        foreach (var ids in produced)
        {
            Assert.Equal(ids, stored.Where(ids.Contains));
        }

        Assert.Equal(0, await h.Store.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Concurrent_flushes_do_not_send_a_statement_twice()
    {
        await using var h = Harness.Create(o => o.BatchSize = 3);
        await h.EnqueueAsync(30);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => h.Outbox.FlushAsync(Ct))));

        Assert.Equal(30, results.Sum(r => r.Delivered));
        Assert.Equal(10, h.Lrs.Requests.Count);
        Assert.Equal(30, h.Lrs.StoredIds.Distinct().Count());
    }
}
