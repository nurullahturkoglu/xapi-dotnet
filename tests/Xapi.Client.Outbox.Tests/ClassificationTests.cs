using System.Net;
using System.Net.Http.Headers;
using Xapi.Client.Outbox.Tests.Fakes;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

/// <summary>One test (or more) per row of the classification table in docs/outbox.md.</summary>
public class ClassificationTests
{
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Transient_status_keeps_the_statements_and_waits_to_retry(int status)
    {
        await using var h = Harness.Create();
        var ids = await h.EnqueueAsync(2);
        h.Lrs.Next((HttpStatusCode)status, "try later");

        var result = await h.Outbox.FlushAsync(Ct);

        AssertWaitingToRetry(result, pending: 2);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Status.LastError, StringComparison.Ordinal);
        var entries = await h.Store.GetPendingAsync(10, Ct);
        Assert.Equal(ids, entries.Select(e => e.StatementId));
        Assert.All(entries, e => Assert.Equal(1, e.Attempts));
        Assert.All(entries, e => Assert.Equal(result.Status.LastError, e.LastError));
    }

    public static TheoryData<string> TransientExceptions => ["network", "timeout", "unusable-success"];

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public async Task Network_errors_timeouts_and_unusable_responses_are_transient(string kind)
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(2);
        _ = kind switch
        {
            "network" => h.Lrs.NextThrows(new HttpRequestException("connection refused")),
            // What HttpClient throws when its Timeout elapses: a cancellation the caller did not ask for
            "timeout" => h.Lrs.NextThrows(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")),
            _ => h.Lrs.Next(HttpStatusCode.OK, "<html>captive portal</html>"),
        };

        var result = await h.Outbox.FlushAsync(Ct);

        AssertWaitingToRetry(result, pending: 2);
    }

    [Fact]
    public async Task Unexpected_exception_from_the_pipeline_keeps_the_statements()
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(1);
        h.Lrs.NextThrows(new InvalidOperationException("token provider failed"));

        var result = await h.Outbox.FlushAsync(Ct);

        AssertWaitingToRetry(result, pending: 1);
        Assert.Contains("token provider failed", result.Status.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Longer_retry_after_is_honoured()
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(1);
        h.Lrs.Next((HttpStatusCode)429, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)));

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(Harness.Start.AddSeconds(120), result.Status.RetryAt);
    }

    [Fact]
    public async Task Retry_after_shorter_than_the_backoff_does_not_make_the_retry_earlier()
    {
        await using var h = Harness.Create(o => o.InitialRetryDelay = TimeSpan.FromSeconds(10));
        h.Outbox.Random = new FixedRandom(0.5);
        await h.EnqueueAsync(1);
        h.Lrs.Next(HttpStatusCode.ServiceUnavailable, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero));

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(Harness.Start.AddSeconds(7.5), result.Status.RetryAt);
    }

    [Fact]
    public async Task Backoff_doubles_up_to_the_maximum_with_jitter()
    {
        await using var h = Harness.Create(o =>
        {
            o.InitialRetryDelay = TimeSpan.FromSeconds(2);
            o.MaxRetryDelay = TimeSpan.FromSeconds(10);
        });
        h.Outbox.Random = new FixedRandom(0.5);
        await h.EnqueueAsync(1);
        h.Lrs.Offline = true;

        var delays = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var result = await h.Outbox.FlushAsync(Ct);
            Assert.Equal(i + 1, result.Status.ConsecutiveFailures);
            delays.Add((result.Status.RetryAt!.Value - Harness.Start).TotalSeconds);
        }

        // 0.75 of 2, 4, 8, 10, 10 seconds
        Assert.Equal([1.5, 3, 6, 7.5, 7.5], delays);
    }

    [Fact]
    public async Task Jittered_delay_stays_between_half_and_all_of_the_exponential_delay()
    {
        await using var h = Harness.Create(o => o.MaxRetryDelay = TimeSpan.FromDays(1));
        await h.EnqueueAsync(1);
        h.Lrs.Offline = true;

        for (var failures = 1; failures <= 8; failures++)
        {
            var result = await h.Outbox.FlushAsync(Ct);
            var full = 2 * Math.Pow(2, failures - 1);
            var delay = (result.Status.RetryAt!.Value - Harness.Start).TotalSeconds;
            Assert.InRange(delay, full / 2, full);
        }
    }

    [Fact]
    public async Task Success_resets_the_failure_count()
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(1);
        h.Lrs.Next(HttpStatusCode.BadGateway).Next(HttpStatusCode.BadGateway);
        await h.Outbox.FlushAsync(Ct);
        await h.Outbox.FlushAsync(Ct);

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(1, result.Delivered);
        Assert.Equal(OutboxState.Idle, result.Status.State);
        Assert.Equal(0, result.Status.ConsecutiveFailures);
        Assert.Null(result.Status.RetryAt);
        Assert.Null(result.Status.LastError);
    }

    [Fact]
    public async Task Bad_request_for_one_statement_dead_letters_it_with_the_response_and_goes_on()
    {
        var lrs = new FakeLrs { Reject = s => Number(s) == 1 ? HttpStatusCode.BadRequest : null };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(2);
        var events = new List<OutboxDeadLetter>();
        h.Outbox.DeadLettered += (_, e) => events.Add(e.DeadLetter);

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, result.DeadLettered);
        var deadLetter = Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.Equal(ids[0], deadLetter.StatementId);
        Assert.Equal(OutboxDeadLetterReason.Rejected, deadLetter.Reason);
        Assert.Equal(HttpStatusCode.BadRequest, deadLetter.StatusCode);
        Assert.Equal($"{{\"error\":\"rejected {ids[0]}\"}}", deadLetter.ResponseBody);
        Assert.Contains("400", deadLetter.Error, StringComparison.Ordinal);
        Assert.Equal(Harness.Start, deadLetter.EnqueuedAt);
        Assert.Equal([deadLetter], events);
        Assert.Equal([ids[1]], h.Lrs.StoredIds);
    }

    [Fact]
    public async Task Bad_request_for_a_batch_is_bisected_to_the_invalid_statement()
    {
        var lrs = new FakeLrs { Reject = s => Number(s) == 6 ? HttpStatusCode.BadRequest : null };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(8);

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(7, result.Delivered);
        Assert.Equal(ids[5], Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct)).StatementId);
        Assert.Equal(ids.Where((_, i) => i != 5), h.Lrs.StoredIds);

        // [1-8] fails, [1-4] ok, [5-8] fails, [5-6] fails, [5] ok, [6] fails, [7-8] ok
        Assert.Equal([8, 4, 4, 2, 1, 1, 2], h.Lrs.Requests.Select(r => r.StatementIds.Count));
    }

    [Fact]
    public async Task Bisection_finds_several_invalid_statements()
    {
        var lrs = new FakeLrs { Reject = s => Number(s) is 1 or 4 or 5 ? (HttpStatusCode)422 : null };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(5);

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(2, result.Delivered);
        Assert.Equal([ids[0], ids[3], ids[4]], (await h.Outbox.GetDeadLettersAsync(Ct)).Select(d => d.StatementId));
        Assert.Equal([ids[1], ids[2]], h.Lrs.StoredIds);
    }

    [Fact]
    public async Task Statement_the_client_rejects_before_sending_is_isolated_without_a_request()
    {
        // contextAgents does not exist in xAPI 1.0.3, so the client's validation refuses the Statement
        await using var h = Harness.Create(version: XapiVersion.V1_0_3);
        var ids = await h.EnqueueAsync(2);
        var twoOnly = Statement(3) with { Context = new Context { ContextAgents = XapiList.Create(new ContextAgent(Learner)) } };
        ids.Add(await h.Outbox.EnqueueAsync(twoOnly, Ct));
        ids.AddRange(await h.EnqueueAsync(1, first: 4));

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(3, result.Delivered);
        var deadLetter = Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.Equal(ids[2], deadLetter.StatementId);
        Assert.Equal(OutboxDeadLetterReason.Rejected, deadLetter.Reason);
        Assert.Null(deadLetter.StatusCode);
        Assert.Contains(nameof(XapiValidationException), deadLetter.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Lrs.Requests, r => r.StatementIds.Contains(ids[2]));
    }

    [Fact]
    public async Task Too_large_batch_is_halved_and_the_smaller_size_is_kept()
    {
        var lrs = new FakeLrs { MaxStatementsPerRequest = 2 };
        await using var h = Harness.Create(o => o.BatchSize = 8, lrs: lrs);
        var ids = await h.EnqueueAsync(8);

        var first = await h.Outbox.FlushAsync(Ct);
        var requestsBefore = h.Lrs.Requests.Count;
        ids.AddRange(await h.EnqueueAsync(4, first: 9));
        var second = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(8, first.Delivered);
        Assert.Equal(4, second.Delivered);
        Assert.Equal(ids, h.Lrs.StoredIds);
        Assert.Equal([2, 2], h.Lrs.Requests.Skip(requestsBefore).Select(r => r.StatementIds.Count));
    }

    [Fact]
    public async Task Too_large_single_statement_is_dead_lettered()
    {
        var lrs = new FakeLrs { Reject = s => Number(s) == 2 ? HttpStatusCode.RequestEntityTooLarge : null };
        await using var h = Harness.Create(lrs: lrs);
        var ids = await h.EnqueueAsync(3);

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(2, result.Delivered);
        var deadLetter = Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.Equal((ids[1], OutboxDeadLetterReason.TooLarge, HttpStatusCode.RequestEntityTooLarge), (deadLetter.StatementId, deadLetter.Reason, deadLetter.StatusCode));
    }

    [Fact]
    public async Task Conflict_with_an_equivalent_stored_statement_counts_as_delivered()
    {
        var lrs = new FakeLrs { ConflictOnEquivalent = true };
        await using var h = Harness.Create(lrs: lrs);
        // Equivalent, not equal: the Verb display and the timestamp are not part of the comparison
        lrs.Store(Statement(1, Id(1)) with { Verb = Verbs.Completed with { Display = LanguageMap.Create("de-DE", "abgeschlossen") }, Timestamp = Harness.Start.AddDays(-1) });

        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);
        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(1, result.Delivered);
        Assert.Equal(0, result.DeadLettered);
        Assert.Equal([HttpMethod.Put, HttpMethod.Get], h.Lrs.Requests.Select(r => r.Method));
        Assert.Equal(Id(1).ToString(), h.Lrs.Requests[1].Query("statementId"));
    }

    [Fact]
    public async Task Conflict_with_a_different_stored_statement_is_dead_lettered()
    {
        await using var h = Harness.Create();
        h.Lrs.Store(Statement(1, Id(1)) with { Verb = Verbs.Failed });

        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);
        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(1, result.DeadLettered);
        var deadLetter = Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.Equal(OutboxDeadLetterReason.Conflict, deadLetter.Reason);
        Assert.Equal(HttpStatusCode.Conflict, deadLetter.StatusCode);
        Assert.Equal($"conflict on {Id(1)}", deadLetter.ResponseBody);
        Assert.Contains("different Statement", deadLetter.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conflict_with_a_voided_equivalent_statement_counts_as_delivered()
    {
        var lrs = new FakeLrs { ConflictOnEquivalent = true };
        await using var h = Harness.Create(lrs: lrs);
        lrs.Store(Statement(1, Id(1)));
        lrs.Voided.Add(Id(1));

        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);
        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(1, result.Delivered);
        Assert.Equal(Id(1).ToString(), h.Lrs.Requests.Last().Query("voidedStatementId"));
    }

    [Fact]
    public async Task Conflict_for_a_statement_the_lrs_cannot_return_is_dead_lettered()
    {
        await using var h = Harness.Create();
        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);
        h.Lrs.Next(HttpStatusCode.Conflict, "conflict");

        var result = await h.Outbox.FlushAsync(Ct);

        var deadLetter = Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.Equal(1, result.DeadLettered);
        Assert.Equal(OutboxDeadLetterReason.Conflict, deadLetter.Reason);
        Assert.Contains("no Statement", deadLetter.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conflict_check_that_fails_transiently_keeps_the_statement()
    {
        await using var h = Harness.Create();
        await h.Outbox.EnqueueAsync(Statement(1, Id(1)), Ct);
        h.Lrs.Next(HttpStatusCode.Conflict, "conflict").Next(HttpStatusCode.ServiceUnavailable);

        var result = await h.Outbox.FlushAsync(Ct);

        AssertWaitingToRetry(result, pending: 1);
    }

    [Fact]
    public async Task Conflict_in_a_batch_falls_back_to_one_statement_at_a_time()
    {
        await using var h = Harness.Create();
        h.Lrs.Store(Statement(2, Id(2)) with { Verb = Verbs.Failed });
        var ids = new List<Guid>();
        for (var i = 1; i <= 3; i++)
        {
            ids.Add(await h.Outbox.EnqueueAsync(Statement(i, Id(i)), Ct));
        }

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(2, result.Delivered);
        Assert.Equal(1, result.DeadLettered);
        Assert.Equal(ids[1], Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct)).StatementId);
        Assert.Equal([Id(2), Id(1), Id(3)], h.Lrs.StoredIds);
        Assert.Equal(
            [(HttpMethod.Post, 3), (HttpMethod.Put, 1), (HttpMethod.Put, 1), (HttpMethod.Get, 0), (HttpMethod.Put, 1)],
            h.Lrs.Requests.Select(r => (r.Method, r.Method == HttpMethod.Get ? 0 : r.StatementIds.Count)));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(501)]
    public async Task Refused_request_pauses_without_dead_letters_and_without_retrying(int status)
    {
        await using var h = Harness.Create(o => o.BatchSize = 2);
        await h.EnqueueAsync(3);
        h.Lrs.Next((HttpStatusCode)status, "nope");
        var paused = new List<Exception>();
        h.Outbox.Paused += (_, e) => paused.Add(e.Error);

        var first = await h.Outbox.FlushAsync(Ct);
        var second = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(OutboxState.Paused, first.Status.State);
        Assert.Equal(3, first.Status.PendingCount);
        Assert.Equal(0, first.Status.DeadLetterCount);
        Assert.Null(first.Status.RetryAt);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), first.Status.LastError, StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode)status, Assert.IsType<XapiHttpException>(Assert.Single(paused)).StatusCode);
        Assert.Equal(OutboxState.Paused, second.Status.State);
        Assert.Single(h.Lrs.Requests);
        Assert.All(await h.Store.GetPendingAsync(10, Ct), e => Assert.Equal(0, e.Attempts));
    }

    [Fact]
    public async Task No_common_xapi_version_pauses()
    {
        await using var h = Harness.Create();
        await h.EnqueueAsync(1);
        h.Lrs.NextThrows(new NotSupportedException("The LRS supports none of the xAPI versions this client speaks."));

        var result = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(OutboxState.Paused, result.Status.State);
    }

    [Fact]
    public async Task Statement_is_dead_lettered_after_max_attempts()
    {
        await using var h = Harness.Create(o => o.MaxAttempts = 2);
        var ids = await h.EnqueueAsync(1);
        h.Lrs.Next(HttpStatusCode.ServiceUnavailable, "down").Next(HttpStatusCode.ServiceUnavailable, "still down");

        var first = await h.Outbox.FlushAsync(Ct);
        var second = await h.Outbox.FlushAsync(Ct);

        Assert.Equal(1, first.Status.PendingCount);
        Assert.Equal(1, second.DeadLettered);
        Assert.Equal(OutboxState.WaitingToRetry, second.Status.State);
        Assert.Equal(2, second.Status.ConsecutiveFailures);
        var deadLetter = Assert.Single(await h.Outbox.GetDeadLettersAsync(Ct));
        Assert.Equal((ids[0], OutboxDeadLetterReason.MaxAttemptsExceeded, 2, "still down"), (deadLetter.StatementId, deadLetter.Reason, deadLetter.Attempts, deadLetter.ResponseBody));
    }

    private static void AssertWaitingToRetry(OutboxFlushResult result, int pending)
    {
        Assert.Equal(0, result.Delivered);
        Assert.Equal(0, result.DeadLettered);
        Assert.Equal(OutboxState.WaitingToRetry, result.Status.State);
        Assert.Equal(pending, result.Status.PendingCount);
        Assert.Equal(0, result.Status.DeadLetterCount);
        Assert.Equal(1, result.Status.ConsecutiveFailures);
        Assert.NotNull(result.Status.LastError);
        Assert.InRange(result.Status.RetryAt!.Value, Harness.Start.AddSeconds(1), Harness.Start.AddSeconds(2));
    }
}
