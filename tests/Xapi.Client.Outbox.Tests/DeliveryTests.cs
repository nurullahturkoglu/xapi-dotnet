using System.Net;
using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

public class DeliveryTests
{
    [Fact]
    public async Task Flush_sends_in_enqueue_order_in_batches()
    {
        await using var h = Harness.Create(o => o.BatchSize = 3);
        var ids = await h.EnqueueAsync(7);

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(7, result.Delivered);
        Assert.Equal(OutboxState.Idle, result.Status.State);
        Assert.Equal(0, result.Status.PendingCount);
        Assert.Equal(ids, h.Lrs.StoredIds);
        Assert.Equal([3, 3, 1], h.Lrs.Requests.Select(r => r.StatementIds.Count));
        Assert.Equal([HttpMethod.Post, HttpMethod.Post, HttpMethod.Put], h.Lrs.Requests.Select(r => r.Method));
        Assert.Equal(ids[6].ToString(), h.Lrs.Requests[2].Query("statementId"));
    }

    [Fact]
    public async Task Flush_of_an_empty_outbox_sends_nothing()
    {
        await using var h = Harness.Create();

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(0, result.Delivered);
        Assert.Empty(h.Lrs.Requests);
    }

    [Fact]
    public async Task Retry_after_a_failure_sends_the_same_ids()
    {
        await using var h = Harness.Create();
        var ids = await h.EnqueueAsync(3);
        h.Lrs.Next(HttpStatusCode.ServiceUnavailable);

        await h.Outbox.FlushAsync(Ct);
        await h.Outbox.FlushAsync(Ct);

        Assert.Equal(2, h.Lrs.Requests.Count);
        Assert.Equal(ids, h.Lrs.Requests[0].StatementIds);
        Assert.Equal(ids, h.Lrs.Requests[1].StatementIds);
        Assert.Equal(ids, h.Lrs.StoredIds);
    }

    [Fact]
    public async Task Lost_response_leads_to_a_resend_that_the_lrs_stores_once()
    {
        await using var h = Harness.Create();
        var ids = await h.EnqueueAsync(3);
        h.Lrs.LoseNextResponse = true;

        var first = await h.Outbox.FlushAsync(Ct);
        var second = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(OutboxState.WaitingToRetry, first.Status.State);
        Assert.Equal(3, second.Delivered);
        Assert.Equal(ids, h.Lrs.StoredIds);
    }

    [Fact]
    public async Task Delivered_event_lists_the_ids_of_each_batch()
    {
        await using var h = Harness.Create(o => o.BatchSize = 2);
        var ids = await h.EnqueueAsync(3);
        var events = new List<IReadOnlyList<Guid>>();
        h.Outbox.Delivered += (_, e) => events.Add(e.StatementIds);
        h.Outbox.Delivered += (_, _) => throw new InvalidOperationException("A failing handler must not stop delivery.");

        await h.Outbox.FlushAsync(Ct);

        Assert.Equal([ids.Take(2).ToList(), ids.Skip(2).ToList()], events.Select(e => e.ToList()));
        Assert.Equal(0, await h.Store.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Statement_enqueued_while_paused_is_kept_and_sent_after_resume()
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(1);
        h.Lrs.Next(HttpStatusCode.Unauthorized);
        await h.Outbox.FlushAsync(Ct);

        await h.EnqueueAsync(1, first: 2);
        var whilePaused = await h.Outbox.FlushAsync(Ct);
        h.Outbox.Resume();
        var resumed = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(OutboxState.Paused, whilePaused.Status.State);
        Assert.Equal(2, whilePaused.Status.PendingCount);
        Assert.Equal(2, resumed.Delivered);
        Assert.Equal(OutboxState.Idle, resumed.Status.State);
        Assert.Null(resumed.Status.LastError);
    }

    [Fact]
    public async Task Disposed_outbox_refuses_new_work()
    {
        var h = Harness.Create();
        await h.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Outbox.EnqueueAsync(Statement(1), Ct).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Outbox.FlushAsync(Ct));
    }
}
