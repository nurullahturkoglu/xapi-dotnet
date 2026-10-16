using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests.Stores;

/// <summary>The <see cref="IOutboxStore"/> contract, run against both built-in stores.</summary>
public sealed class StoreContractTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private FileOutboxStore? _file;

    public static TheoryData<string> Kinds => ["memory", "file"];

    public void Dispose()
    {
        _file?.Dispose();
        _directory.Dispose();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Pending_entries_come_back_in_order_up_to_the_maximum(string kind)
    {
        var store = Create(kind);
        for (var i = 1; i <= 5; i++)
        {
            Assert.True(await store.AddAsync(Entry(i), Ct));
        }

        Assert.Equal([Id(1), Id(2), Id(3)], (await store.GetPendingAsync(3, Ct)).Select(e => e.StatementId));
        Assert.Equal(5, await store.GetPendingCountAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Adding_a_pending_id_again_is_refused(string kind)
    {
        var store = Create(kind);
        await store.AddAsync(Entry(1), Ct);

        Assert.False(await store.AddAsync(Entry(1), Ct));
        Assert.Equal(1, await store.GetPendingCountAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Remove_ignores_unknown_ids(string kind)
    {
        var store = Create(kind);
        await store.AddAsync(Entry(1), Ct);
        await store.AddAsync(Entry(2), Ct);

        await store.RemoveAsync([Id(1), Id(9)], Ct);

        Assert.Equal([Id(2)], (await store.GetPendingAsync(10, Ct)).Select(e => e.StatementId));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Failed_attempts_are_counted_with_the_last_error(string kind)
    {
        var store = Create(kind);
        await store.AddAsync(Entry(1), Ct);

        await store.RecordFailedAttemptAsync([Id(1)], "first", Ct);
        await store.RecordFailedAttemptAsync([Id(1), Id(9)], "second", Ct);

        var entry = Assert.Single(await store.GetPendingAsync(10, Ct));
        Assert.Equal((2, "second"), (entry.Attempts, entry.LastError));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Dead_letter_leaves_the_queue_and_can_be_requeued_or_removed(string kind)
    {
        var store = Create(kind);
        for (var i = 1; i <= 3; i++)
        {
            await store.AddAsync(Entry(i), Ct);
        }

        await store.MoveToDeadLettersAsync(DeadLetter(1), Ct);
        await store.MoveToDeadLettersAsync(DeadLetter(2), Ct);

        Assert.Equal([Id(3)], (await store.GetPendingAsync(10, Ct)).Select(e => e.StatementId));
        Assert.Equal([Id(1), Id(2)], (await store.GetDeadLettersAsync(Ct)).Select(d => d.StatementId));
        Assert.Equal(2, await store.GetDeadLetterCountAsync(Ct));

        Assert.True(await store.RequeueAsync(Id(1), Ct));
        Assert.False(await store.RequeueAsync(Id(1), Ct));
        Assert.True(await store.RemoveDeadLetterAsync(Id(2), Ct));
        Assert.False(await store.RemoveDeadLetterAsync(Id(2), Ct));

        var pending = await store.GetPendingAsync(10, Ct);
        Assert.Equal([Id(3), Id(1)], pending.Select(e => e.StatementId));
        Assert.Equal((0, Harness.Start), (pending[1].Attempts, pending[1].EnqueuedAt));
        Assert.Equal(0, await store.GetDeadLetterCountAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Requeue_of_an_id_that_is_pending_again_only_removes_the_dead_letter(string kind)
    {
        var store = Create(kind);
        await store.MoveToDeadLettersAsync(DeadLetter(1), Ct);
        await store.AddAsync(Entry(1), Ct);

        Assert.True(await store.RequeueAsync(Id(1), Ct));

        Assert.Equal(1, await store.GetPendingCountAsync(Ct));
        Assert.Equal(0, await store.GetDeadLetterCountAsync(Ct));
    }

    internal static OutboxEntry Entry(int n) => new(Statement(n, Id(n)) with { Timestamp = Harness.Start }, Harness.Start);

    internal static OutboxDeadLetter DeadLetter(int n) =>
        new(Statement(n, Id(n)) with { Timestamp = Harness.Start }, OutboxDeadLetterReason.Rejected, "400 Bad Request", Harness.Start.AddMinutes(1))
        {
            EnqueuedAt = Harness.Start,
            StatusCode = System.Net.HttpStatusCode.BadRequest,
            ResponseBody = "{\"error\":\"bad\"}",
            Attempts = 1,
        };

    private IOutboxStore Create(string kind)
    {
        if (kind == "file")
        {
            _file = new FileOutboxStore(_directory.Path);
            return _file;
        }

        return new InMemoryOutboxStore();
    }
}
