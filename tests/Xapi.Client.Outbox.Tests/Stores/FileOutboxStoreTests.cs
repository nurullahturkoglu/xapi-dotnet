using System.Net;
using System.Text;
using System.Text.Json;
using Xapi.Client.Outbox.Tests.Fakes;
using Xapi.Json;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;
using static Xapi.Client.Outbox.Tests.Stores.StoreContractTests;

namespace Xapi.Client.Outbox.Tests.Stores;

public sealed class FileOutboxStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Queue_dead_letters_and_attempts_survive_reopening()
    {
        using (var store = new FileOutboxStore(_directory.Path))
        {
            for (var i = 1; i <= 4; i++)
            {
                await store.AddAsync(Entry(i), Ct);
            }

            await store.RemoveAsync([Id(1)], Ct);
            await store.RecordFailedAttemptAsync([Id(2)], "503 Service Unavailable", Ct);
            await store.MoveToDeadLettersAsync(DeadLetter(3), Ct);
            Assert.Null(store.Recovery);
        }

        using var reopened = new FileOutboxStore(_directory.Path);

        Assert.Null(reopened.Recovery);
        var pending = await reopened.GetPendingAsync(10, Ct);
        Assert.Equal([Entry(2) with { Attempts = 1, LastError = "503 Service Unavailable" }, Entry(4)], pending);
        Assert.Equal([DeadLetter(3)], await reopened.GetDeadLettersAsync(Ct));
    }

    [Fact]
    public async Task Statement_round_trips_through_the_journal_unchanged()
    {
        var extension = JsonDocument.Parse("""{"nested":[1,2.50,"x"],"b":true}""").RootElement.Clone();
        var statement = new Statement(Learner, Verbs.Completed, new SubStatement(Learner, Verbs.Attempted, new Activity(ActivityId(1))))
        {
            Id = Id(1),
            Timestamp = new DateTimeOffset(2026, 10, 17, 9, 30, 15, 123, TimeSpan.Zero).AddTicks(4567),
            Result = new Result { Duration = XapiDuration.Parse("PT1M30.5S"), Response = "naïve “quotes”\nand a newline" },
            Context = new Context
            {
                Registration = Id(2),
                ContextAgents = XapiList.Create(new ContextAgent(Learner)),
                Extensions = Extensions.Create(new Iri("https://example.com/ext"), extension),
            },
        };
        using (var store = new FileOutboxStore(_directory.Path))
        {
            await store.AddAsync(new OutboxEntry(statement, Harness.Start), Ct);
        }

        using (var reopened = new FileOutboxStore(_directory.Path))
        {
            var read = Assert.Single(await reopened.GetPendingAsync(1, Ct)).Statement;
            Assert.Equal(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), XapiJson.SerializeStatement(read, XapiVersion.V2_0_0));
        }

        // Read after the store is closed: on Windows the open journal only admits readers that allow writing
        Assert.Single(File.ReadAllLines(_directory.Journal));
    }

    [Fact]
    public async Task Write_cut_off_at_any_byte_loses_only_the_unacknowledged_record()
    {
        // The journal as it is after three acknowledged adds and a fourth add that was being written
        using (var store = new FileOutboxStore(_directory.Path))
        {
            for (var i = 1; i <= 4; i++)
            {
                await store.AddAsync(Entry(i), Ct);
            }
        }

        var full = File.ReadAllBytes(_directory.Journal);
        var lastLineStart = Array.LastIndexOf(full, (byte)'\n', full.Length - 2) + 1;

        for (var cut = lastLineStart; cut < full.Length; cut++)
        {
            using var crashed = new TempDirectory();
            File.WriteAllBytes(crashed.Journal, full.AsSpan(0, cut).ToArray());

            using (var store = new FileOutboxStore(crashed.Path))
            {
                Assert.Equal([Id(1), Id(2), Id(3)], (await store.GetPendingAsync(10, Ct)).Select(e => e.StatementId));
                Assert.Equal(cut == lastLineStart ? null : new FileOutboxRecovery(0, cut - lastLineStart, null), store.Recovery);
                Assert.Equal(lastLineStart, new FileInfo(crashed.Journal).Length);

                // Later writes start on a clean line
                await store.AddAsync(Entry(5), Ct);
            }

            using var reopened = new FileOutboxStore(crashed.Path);
            Assert.Null(reopened.Recovery);
            Assert.Equal([Id(1), Id(2), Id(3), Id(5)], (await reopened.GetPendingAsync(10, Ct)).Select(e => e.StatementId));
        }
    }

    [Fact]
    public async Task Damaged_line_is_skipped_reported_and_kept_in_a_copy()
    {
        using (var store = new FileOutboxStore(_directory.Path))
        {
            for (var i = 1; i <= 3; i++)
            {
                await store.AddAsync(Entry(i), Ct);
            }
        }

        var original = File.ReadAllBytes(_directory.Journal);
        var damaged = (byte[])original.Clone();
        var secondLine = Array.IndexOf(damaged, (byte)'\n') + 1;
        var target = Encoding.UTF8.GetBytes("activities/2");
        var at = damaged.AsSpan(secondLine).IndexOf(target) + secondLine + target.Length - 1;
        damaged[at] = (byte)'7';
        File.WriteAllBytes(_directory.Journal, damaged);

        using (var store = new FileOutboxStore(_directory.Path))
        {
            Assert.Equal([Id(1), Id(3)], (await store.GetPendingAsync(10, Ct)).Select(e => e.StatementId));
            Assert.Equal(1, store.Recovery!.UnreadableRecords);
            Assert.Equal(0, store.Recovery.DiscardedTailBytes);
            Assert.Equal(damaged, File.ReadAllBytes(store.Recovery.CorruptJournalCopyPath!));
        }

        // The journal was rewritten without the damaged line
        using var reopened = new FileOutboxStore(_directory.Path);
        Assert.Null(reopened.Recovery);
        Assert.Equal(2, await reopened.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Leftover_file_of_an_interrupted_compaction_is_ignored()
    {
        using (var store = new FileOutboxStore(_directory.Path))
        {
            await store.AddAsync(Entry(1), Ct);
        }

        File.WriteAllText(_directory.Journal + ".tmp", "{\"crc\":\"00000000\",\"rec");

        using var reopened = new FileOutboxStore(_directory.Path);
        Assert.Equal(1, await reopened.GetPendingCountAsync(Ct));
        Assert.False(File.Exists(_directory.Journal + ".tmp"));
    }

    [Fact]
    public async Task Journal_is_compacted_and_stays_small_under_churn()
    {
        using (var store = new FileOutboxStore(_directory.Path, compactionThreshold: 4096))
        {
            for (var round = 0; round < 50; round++)
            {
                for (var i = 0; i < 5; i++)
                {
                    await store.AddAsync(Entry((round * 5) + i), Ct);
                }

                await store.RecordFailedAttemptAsync([Id(round * 5)], "transient", Ct);
                await store.RemoveAsync([.. Enumerable.Range(round * 5, 4).Select(Id)], Ct);
            }
        }

        // Without compaction the journal would have 350 lines: 250 adds, 50 attempts and 50 removals
        Assert.InRange(File.ReadAllLines(_directory.Journal).Length, 50, 175);
        using var reopened = new FileOutboxStore(_directory.Path);
        var pending = await reopened.GetPendingAsync(100, Ct);
        Assert.Equal(Enumerable.Range(0, 50).Select(round => Id((round * 5) + 4)), pending.Select(e => e.StatementId));
        Assert.False(File.Exists(_directory.Journal + ".tmp"));
    }

    [Fact]
    public void Second_store_on_the_same_directory_is_refused()
    {
        using (var store = new FileOutboxStore(_directory.Path))
        {
            var exception = Assert.Throws<IOException>(() => new FileOutboxStore(_directory.Path));
            Assert.Contains("in use", exception.Message, StringComparison.Ordinal);
        }

        using var afterDispose = new FileOutboxStore(_directory.Path);
    }

    [Fact]
    public async Task Disposed_store_refuses_calls()
    {
        var store = new FileOutboxStore(_directory.Path);
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.AddAsync(Entry(1), Ct).AsTask());
    }

    [Fact]
    public async Task Outbox_on_a_reopened_store_delivers_what_an_earlier_process_queued()
    {
        List<Guid> ids;
        var lrs = new FakeLrs { Reject = s => Number(s) == 2 ? HttpStatusCode.BadRequest : null, Offline = true };
        using (var store = new FileOutboxStore(_directory.Path))
        {
            await using var first = Harness.Create(store: store, lrs: lrs);
            ids = await first.EnqueueAsync(3);
            await first.Outbox.FlushAsync(Ct);
        }

        lrs.Offline = false;
        using (var store = new FileOutboxStore(_directory.Path))
        {
            Assert.All(await store.GetPendingAsync(10, Ct), e => Assert.Equal(1, e.Attempts));
            await using var second = Harness.Create(store: store, lrs: lrs);
            var result = await second.Outbox.FlushAsync(Ct);
            Assert.Equal(2, result.Delivered);
        }

        using var third = new FileOutboxStore(_directory.Path);
        Assert.Equal([ids[0], ids[2]], lrs.StoredIds);
        Assert.Equal(0, await third.GetPendingCountAsync(Ct));
        Assert.Equal(ids[1], Assert.Single(await third.GetDeadLettersAsync(Ct)).StatementId);
    }
}
