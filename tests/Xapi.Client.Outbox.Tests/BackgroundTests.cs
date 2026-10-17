using System.Net;
using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

public class BackgroundTests
{
    [Fact]
    public async Task Loop_delivers_what_was_queued_before_it_started_and_what_is_enqueued_later()
    {
        await using var h = Harness.Create();
        var ids = await h.EnqueueAsync(2);
        using var stop = new CancellationTokenSource();

        var loop = h.Outbox.RunAsync(stop.Token);
        await Eventually(() => h.Lrs.StoredIds.Count == 2, "the queued Statements are delivered");
        ids.AddRange(await h.EnqueueAsync(1, first: 3));
        await Eventually(() => h.Lrs.StoredIds.Count == 3, "the new Statement is delivered");

        stop.Cancel();
        await loop;
        Assert.Equal(ids, h.Lrs.StoredIds);
    }

    [Fact]
    public async Task Loop_waits_for_the_retry_time_on_the_clock()
    {
        await using var h = Harness.Create();
        h.Outbox.Random = new FixedRandom(0.5);
        await h.EnqueueAsync(1);
        h.Lrs.Next(HttpStatusCode.ServiceUnavailable);

        var loop = h.Outbox.RunAsync(Ct);
        await Eventually(async () => (await h.Outbox.GetStatusAsync(Ct)).State == OutboxState.WaitingToRetry, "the first send fails");
        Assert.Equal(Harness.Start.AddSeconds(1.5), (await h.Outbox.GetStatusAsync(Ct)).RetryAt);

        h.Time.Advance(TimeSpan.FromSeconds(1.4));
        await Task.Delay(100, Ct);
        Assert.Single(h.Lrs.Requests);

        h.Time.Advance(TimeSpan.FromSeconds(0.1));
        await Eventually(() => h.Lrs.StoredIds.Count == 1, "the retry delivers");
        Assert.Equal(2, h.Lrs.Requests.Count);

        await h.Outbox.DisposeAsync();
        await loop;
    }

    [Fact]
    public async Task Paused_loop_does_not_retry_until_resumed()
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(1);
        h.Lrs.Next(HttpStatusCode.Forbidden);

        var loop = h.Outbox.RunAsync(Ct);
        await Eventually(async () => (await h.Outbox.GetStatusAsync(Ct)).State == OutboxState.Paused, "the outbox pauses");
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.EnqueueAsync(1, first: 2);
        await Task.Delay(100, Ct);
        Assert.Single(h.Lrs.Requests);

        h.Outbox.Resume();
        await Eventually(() => h.Lrs.StoredIds.Count == 2, "the outbox delivers after Resume");

        await h.Outbox.DisposeAsync();
        await loop;
    }

    [Fact]
    public async Task Loop_survives_store_failures()
    {
        var store = new FailingStore();
        await using var h = Harness.Create(store: store);
        await h.EnqueueAsync(1);
        store.FailReads = true;

        var loop = h.Outbox.RunAsync(Ct);
        await Eventually(async () => (await h.Outbox.GetStatusAsync(Ct)).State == OutboxState.WaitingToRetry, "the store failure is reported");
        Assert.Contains("disk on fire", (await h.Outbox.GetStatusAsync(Ct)).LastError, StringComparison.Ordinal);

        store.FailReads = false;
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Eventually(() => h.Lrs.StoredIds.Count == 1, "the loop recovers");

        await h.Outbox.DisposeAsync();
        await loop;
    }

    [Fact]
    public async Task Second_loop_is_refused_and_cancellation_ends_the_loop_quietly()
    {
        await using var h = Harness.Create();
        using var stop = new CancellationTokenSource();

        var loop = h.Outbox.RunAsync(stop.Token);

        Assert.Throws<InvalidOperationException>(() => { _ = h.Outbox.RunAsync(Ct); });
        stop.Cancel();
        await loop;
        var again = h.Outbox.RunAsync(Ct);
        await h.Outbox.DisposeAsync();
        await again;
    }

    private sealed class FailingStore : OutboxStore
    {
        private readonly InMemoryOutboxStore _inner = new();

        public volatile bool FailReads;

        public override ValueTask<bool> AddAsync(OutboxEntry entry, CancellationToken cancellationToken) => _inner.AddAsync(entry, cancellationToken);

        public override ValueTask<IReadOnlyList<OutboxEntry>> GetPendingAsync(int maxCount, CancellationToken cancellationToken) =>
            FailReads ? throw new IOException("disk on fire") : _inner.GetPendingAsync(maxCount, cancellationToken);

        public override ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken) => _inner.GetPendingCountAsync(cancellationToken);

        public override ValueTask RemoveAsync(IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken) => _inner.RemoveAsync(statementIds, cancellationToken);

        public override ValueTask RecordFailedAttemptAsync(IReadOnlyCollection<Guid> statementIds, string error, CancellationToken cancellationToken) =>
            _inner.RecordFailedAttemptAsync(statementIds, error, cancellationToken);

        public override ValueTask MoveToDeadLettersAsync(OutboxDeadLetter deadLetter, CancellationToken cancellationToken) => _inner.MoveToDeadLettersAsync(deadLetter, cancellationToken);

        public override ValueTask<IReadOnlyList<OutboxDeadLetter>> GetDeadLettersAsync(CancellationToken cancellationToken) => _inner.GetDeadLettersAsync(cancellationToken);

        public override ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken) => _inner.GetDeadLetterCountAsync(cancellationToken);

        public override ValueTask<bool> RequeueAsync(Guid statementId, CancellationToken cancellationToken) => _inner.RequeueAsync(statementId, cancellationToken);

        public override ValueTask<bool> RemoveDeadLetterAsync(Guid statementId, CancellationToken cancellationToken) => _inner.RemoveDeadLetterAsync(statementId, cancellationToken);
    }
}
