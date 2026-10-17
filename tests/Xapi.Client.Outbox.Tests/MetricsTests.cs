using System.Diagnostics.Metrics;
using System.Net;
using Xapi.Client.Outbox.Internal;
using Xapi.Client.Outbox.Tests.Fakes;
using Xapi.Testing;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests;

public sealed class MetricsTests : IDisposable
{
    // A meter of each test's own, so outboxes in other tests do not add to the counts
    private readonly Meter _meter = new(XapiClientDiagnostics.MeterName);
    private readonly MetricRecorder _metrics;

    public MetricsTests()
    {
        _metrics = new MetricRecorder(i => i.Meter == _meter);
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public async Task Transient_failures_count_the_statements_kept_for_a_retry_by_error_type()
    {
        var lrs = new FakeLrs();
        await using var h = Harness.Create(lrs: lrs, metrics: new OutboxMetrics(_meter));
        await h.EnqueueAsync(3);

        lrs.Next(HttpStatusCode.ServiceUnavailable);
        await h.Outbox.FlushAsync(Ct);
        lrs.Offline = true;
        await h.Outbox.FlushAsync(Ct);

        Assert.Equal(
            new Dictionary<string, double> { ["error.type=503"] = 3, ["error.type=System.Net.Http.HttpRequestException"] = 3 },
            _metrics.Sums(OutboxDiagnostics.StatementsRetried));
        Assert.All(_metrics.Of(OutboxDiagnostics.StatementsRetried), m => Assert.Equal("{statement}", m.Unit));
        Assert.Empty(_metrics.Of(OutboxDiagnostics.StatementsDeadLettered));
    }

    [Fact]
    public async Task Dead_letters_are_counted_by_reason_and_are_not_counted_as_retried()
    {
        var lrs = new FakeLrs { Reject = s => Number(s) == 1 ? HttpStatusCode.BadRequest : null };
        await using var h = Harness.Create(o => o.MaxAttempts = 1, lrs: lrs, metrics: new OutboxMetrics(_meter));
        await h.EnqueueAsync(2);
        await h.Outbox.FlushAsync(Ct);

        await h.EnqueueAsync(1, first: 3);
        lrs.Next(HttpStatusCode.ServiceUnavailable);
        await h.Outbox.FlushAsync(Ct);

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["xapi.outbox.dead_letter.reason=max_attempts_exceeded"] = 1,
                ["xapi.outbox.dead_letter.reason=rejected"] = 1,
            },
            _metrics.Sums(OutboxDiagnostics.StatementsDeadLettered));
        Assert.Empty(_metrics.Of(OutboxDiagnostics.StatementsRetried));
    }

    [Fact]
    public async Task Pending_statements_of_every_live_outbox_on_the_meter_are_observed()
    {
        var metrics = new OutboxMetrics(_meter);
        var first = Harness.Create(metrics: metrics);
        await using var second = Harness.Create(metrics: metrics);

        await first.EnqueueAsync(4);
        Assert.Equal(4, Assert.Single(_metrics.Observe(OutboxDiagnostics.StatementsPending)).Value);

        await second.EnqueueAsync(3);
        await first.Outbox.FlushAsync(Ct);
        var pending = Assert.Single(_metrics.Observe(OutboxDiagnostics.StatementsPending));
        Assert.Equal(3, pending.Value);
        Assert.Equal("{statement}", pending.Unit);
        Assert.Empty(pending.Tags);

        await first.EnqueueAsync(5, first: 10);
        await first.DisposeAsync();
        Assert.Equal(3, Assert.Single(_metrics.Observe(OutboxDiagnostics.StatementsPending)).Value);
    }

    [Fact]
    public async Task Outbox_created_with_the_public_constructor_reports_on_the_shared_meter()
    {
        // Other tests' outboxes report on the shared meter too, so only this outbox's dead letter is looked for
        using var shared = new MetricRecorder(i => i.Meter.Name == XapiClientDiagnostics.MeterName && i.Meter != _meter && i.Meter.Scope is null);
        var lrs = new FakeLrs { Reject = _ => (HttpStatusCode)422 };
        await using var h = Harness.Create(lrs: lrs);
        await h.EnqueueAsync(1);

        await h.Outbox.FlushAsync(Ct);

        Assert.Contains(shared.Of(OutboxDiagnostics.StatementsDeadLettered), m => m.TagText == "xapi.outbox.dead_letter.reason=rejected");
    }
}
