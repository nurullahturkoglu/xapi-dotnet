using System.Diagnostics.Metrics;
using System.Globalization;

namespace Xapi.Client.Outbox.Internal;

// The outbox's instruments on the Xapi.Client meter (ADR-014). An outbox created with its public constructor uses a
// meter shared by the process; AddOutbox creates one through the host's IMeterFactory. The pending count is
// the sum over the outboxes on the same meter: an up-down counter rather than a gauge, because the values add up.
internal sealed class OutboxMetrics
{
    private readonly Counter<long> _retried;
    private readonly Counter<long> _deadLettered;
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif

    // Weak, so an outbox that is dropped without being disposed is not kept alive by the meter
    private readonly List<WeakReference<StatementOutbox>> _outboxes = [];

    public OutboxMetrics(Meter meter)
    {
        _retried = meter.CreateCounter<long>(
            OutboxDiagnostics.StatementsRetried,
            unit: "{statement}",
            description: "Statements whose send failed with a transient error and that stay queued to be sent again.");
        _deadLettered = meter.CreateCounter<long>(
            OutboxDiagnostics.StatementsDeadLettered,
            unit: "{statement}",
            description: "Statements moved to the dead letters.");
        meter.CreateObservableUpDownCounter(
            OutboxDiagnostics.StatementsPending,
            ObservePending,
            unit: "{statement}",
            description: "Statements waiting in the outbox to be delivered.");
    }

    public static OutboxMetrics Shared { get; } = new(new Meter(XapiClientDiagnostics.MeterName));

    public void Add(StatementOutbox outbox)
    {
        lock (_gate)
        {
            _outboxes.RemoveAll(r => !r.TryGetTarget(out _));
            _outboxes.Add(new WeakReference<StatementOutbox>(outbox));
        }
    }

    public void Remove(StatementOutbox outbox)
    {
        lock (_gate)
        {
            _outboxes.RemoveAll(r => !r.TryGetTarget(out var target) || ReferenceEquals(target, outbox));
        }
    }

    public void Retried(int count, Exception error)
    {
        if (count > 0 && _retried.Enabled)
        {
            _retried.Add(count, new KeyValuePair<string, object?>("error.type", ErrorType(error)));
        }
    }

    public void DeadLettered(OutboxDeadLetterReason reason)
    {
        if (_deadLettered.Enabled)
        {
            _deadLettered.Add(1, new KeyValuePair<string, object?>("xapi.outbox.dead_letter.reason", Reason(reason)));
        }
    }

    private long ObservePending()
    {
        List<StatementOutbox> outboxes;
        lock (_gate)
        {
            outboxes = new List<StatementOutbox>(_outboxes.Count);
            foreach (var reference in _outboxes)
            {
                if (reference.TryGetTarget(out var outbox))
                {
                    outboxes.Add(outbox);
                }
            }
        }

        long pending = 0;
        foreach (var outbox in outboxes)
        {
            pending += outbox.PendingCountOrZero();
        }

        return pending;
    }

    // OpenTelemetry's error.type: the status code of an LRS error response, otherwise the exception type
    private static string ErrorType(Exception error) => error is XapiHttpException { StatusCode: var status } && (int)status >= 300
        ? ((int)status).ToString(CultureInfo.InvariantCulture)
        : error.GetType().FullName ?? "_OTHER";

    private static string Reason(OutboxDeadLetterReason reason) => reason switch
    {
        OutboxDeadLetterReason.Rejected => "rejected",
        OutboxDeadLetterReason.TooLarge => "too_large",
        OutboxDeadLetterReason.Conflict => "conflict",
        OutboxDeadLetterReason.MaxAttemptsExceeded => "max_attempts_exceeded",
        _ => "_OTHER",
    };
}
