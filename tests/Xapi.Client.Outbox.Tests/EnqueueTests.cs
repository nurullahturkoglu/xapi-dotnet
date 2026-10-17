using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

public class EnqueueTests
{
    [Fact]
    public async Task Statement_without_id_gets_a_uuid_v7_and_is_stored_before_returning()
    {
        await using var h = Harness.Create();

        var id = await h.Outbox.EnqueueAsync(Statement(1), Ct);

        Assert.Equal('7', id.ToString("N")[12]);
        Assert.Equal(Harness.Start, UuidV7Generator.GetTimestamp(id));
        var entry = Assert.Single(await h.Store.GetPendingAsync(10, Ct));
        Assert.Equal(id, entry.StatementId);
        Assert.Equal(Harness.Start, entry.EnqueuedAt);
        Assert.Empty(h.Lrs.Requests);
    }

    [Fact]
    public async Task Ids_increase_in_enqueue_order_even_within_one_millisecond()
    {
        await using var h = Harness.Create();

        var ids = await h.EnqueueAsync(20);

        Assert.Equal(ids.OrderBy(id => id.ToString(), StringComparer.Ordinal), ids);
    }

    [Fact]
    public async Task Given_id_is_kept()
    {
        await using var h = Harness.Create();

        var id = await h.Outbox.EnqueueAsync(Statement(1, Id(42)), Ct);

        Assert.Equal(Id(42), id);
    }

    [Fact]
    public async Task Missing_timestamp_is_set_to_the_enqueue_time_in_milliseconds()
    {
        await using var h = Harness.Create();
        h.Time.Advance(TimeSpan.FromTicks(12_345));
        var given = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(3));

        await h.Outbox.EnqueueAsync(Statement(1), Ct);
        await h.Outbox.EnqueueAsync(Statement(2) with { Timestamp = given }, Ct);

        var entries = await h.Store.GetPendingAsync(10, Ct);
        Assert.Equal(Harness.Start.AddMilliseconds(1), entries[0].Statement.Timestamp);
        Assert.Equal(given, entries[1].Statement.Timestamp);
    }

    [Fact]
    public async Task Same_id_twice_is_stored_once()
    {
        await using var h = Harness.Create();

        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);
        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);

        Assert.Equal(1, await h.Store.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Invalid_statement_is_rejected_and_not_stored()
    {
        await using var h = Harness.Create();
        var invalid = Statement(1) with { Version = "3.0.0" };

        var exception = await Assert.ThrowsAsync<XapiValidationException>(() => h.Outbox.EnqueueAsync(invalid, Ct).AsTask());

        Assert.NotEmpty(exception.Errors);
        Assert.Equal(0, await h.Store.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Attachment_without_file_url_is_rejected()
    {
        await using var h = Harness.Create();
        var attachment = new Attachment(new Iri("http://adlnet.gov/expapi/attachments/signature"), LanguageMap.Create("en-US", "signature"), "application/octet-stream", 3, new string('a', 64));
        var withData = Statement(1) with { Attachments = XapiList.Create(attachment) };
        var withUrl = Statement(2) with { Attachments = XapiList.Create(attachment with { FileUrl = new Iri("https://example.com/files/1") }) };

        var exception = await Assert.ThrowsAsync<XapiValidationException>(() => h.Outbox.EnqueueAsync(withData, Ct).AsTask());
        await h.Outbox.EnqueueAsync(withUrl, Ct);

        var error = Assert.Single(exception.Errors);
        Assert.Equal(XapiErrorCodes.AttachmentPayloadMissing, error.Code);
        Assert.Equal("$.attachments[0]", error.Path);
        Assert.Equal(1, await h.Store.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Full_queue_throws_and_keeps_what_is_queued()
    {
        await using var h = Harness.Create(o => o.MaxQueueSize = 3);
        await h.EnqueueAsync(3);

        await Assert.ThrowsAsync<OutboxFullException>(() => h.Outbox.EnqueueAsync(Statement(4), Ct).AsTask());
        Assert.Equal(3, await h.Store.GetPendingCountAsync(Ct));

        await h.Outbox.FlushAsync(Ct);
        await h.Outbox.EnqueueAsync(Statement(4), Ct);
        Assert.Equal(1, await h.Store.GetPendingCountAsync(Ct));
    }

    [Fact]
    public async Task Dead_letters_do_not_count_towards_the_queue_size()
    {
        var lrs = new FakeLrs { Reject = _ => System.Net.HttpStatusCode.BadRequest };
        await using var h = Harness.Create(o => o.MaxQueueSize = 1, lrs: lrs);
        await h.EnqueueAsync(1);
        await h.Outbox.FlushAsync(Ct);

        await h.Outbox.EnqueueAsync(Statement(2), Ct);

        Assert.Equal(1, await h.Store.GetDeadLetterCountAsync(Ct));
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, null, 0)]
    public void Out_of_range_options_are_rejected(int batchSize, int? maxQueueSize, int? maxAttempts)
    {
        var options = new OutboxOptions { BatchSize = batchSize, MaxQueueSize = maxQueueSize, MaxAttempts = maxAttempts };

        Assert.Throws<ArgumentException>(() => new StatementOutbox(Harness.Create().Client, new InMemoryOutboxStore(), options));
    }
}
