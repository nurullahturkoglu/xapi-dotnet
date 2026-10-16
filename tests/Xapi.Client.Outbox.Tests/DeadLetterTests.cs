using System.Net;
using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

public class DeadLetterTests
{
    [Fact]
    public async Task Requeued_dead_letter_goes_to_the_end_of_the_queue_and_is_delivered()
    {
        var reject = true;
        var lrs = new FakeLrs { Reject = s => reject && Number(s) == 1 ? HttpStatusCode.BadRequest : null };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(1);
        await h.Outbox.FlushAsync(Ct);
        ids.AddRange(await h.EnqueueAsync(1, first: 2));

        reject = false;
        Assert.True(await h.Outbox.RequeueDeadLetterAsync(ids[0], Ct));
        var pending = await h.Store.GetPendingAsync(10, Ct);
        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal([ids[1], ids[0]], pending.Select(e => e.StatementId));
        Assert.Equal(0, pending[1].Attempts);
        Assert.Equal(2, result.Delivered);
        Assert.Empty(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.False(await h.Outbox.RequeueDeadLetterAsync(ids[0], Ct));
    }

    [Fact]
    public async Task All_dead_letters_can_be_requeued_at_once()
    {
        var lrs = new FakeLrs { Reject = _ => HttpStatusCode.BadRequest };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(3);
        await h.Outbox.FlushAsync(Ct);

        var count = await h.Outbox.RequeueAllDeadLettersAsync(Ct);

        Assert.Equal(3, count);
        Assert.Equal(ids, (await h.Store.GetPendingAsync(10, Ct)).Select(e => e.StatementId));
    }

    [Fact]
    public async Task Dead_letter_can_be_removed()
    {
        var lrs = new FakeLrs { Reject = _ => HttpStatusCode.BadRequest };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(1);
        await h.Outbox.FlushAsync(Ct);

        Assert.True(await h.Outbox.RemoveDeadLetterAsync(ids[0], Ct));
        Assert.False(await h.Outbox.RemoveDeadLetterAsync(ids[0], Ct));
        Assert.Equal(0, (await h.Outbox.GetStatusAsync(Ct)).DeadLetterCount);
    }
}
