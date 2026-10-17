using Xapi.Client.Outbox.Internal;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Client.Outbox;

public sealed partial class StatementOutbox
{
    /// <summary>Sends until the queue is empty or a send stops the flush. The caller holds <see cref="_flushGate"/>.</summary>
    private async Task<FlushCounts> FlushCoreAsync(CancellationToken cancellationToken)
    {
        var counts = new FlushCounts();
        SetSending(true);
        try
        {
            var client = _clientFactory();
            while (!IsPaused)
            {
                var batch = await _store.GetPendingAsync(_batchSize, cancellationToken).ConfigureAwait(false);
                if (batch.Count == 0 || !await SendAsync(client, batch, counts, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            SetSending(false);
        }

        return counts;
    }

    /// <summary>
    /// Sends a run of consecutive entries and settles every one of them: delivered, dead-lettered, or kept after a
    /// failure that stops the flush.
    /// </summary>
    /// <returns><see langword="false"/> when the flush must stop (transient failure or pause).</returns>
    private async Task<bool> SendAsync(IXapiClient client, IReadOnlyList<OutboxEntry> entries, FlushCounts counts, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            if (entries.Count == 1)
            {
                await client.SaveStatementAsync(entries[0].Statement, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await client.SaveStatementsAsync(entries.Select(e => e.Statement).ToList(), null, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Every failure is classified below; see FailureClassifier
        catch (Exception exception)
#pragma warning restore CA1031
        {
            error = exception;
        }

        if (error is null)
        {
            await DeliveredAsync(entries, counts).ConfigureAwait(false);
            return true;
        }

        switch (FailureClassifier.Classify(error))
        {
            case FailureKind.Transient:
                await FailedAsync(entries, error, counts).ConfigureAwait(false);
                return false;

            case FailureKind.Pause:
                Pause(error);
                return false;

            case FailureKind.Conflict when entries.Count == 1:
                return await ResolveConflictAsync(client, entries[0], (XapiHttpException)error, counts, cancellationToken).ConfigureAwait(false);

            case FailureKind.Conflict:
                // A batch 409 does not say which Statement conflicted. One at a time, the equivalent ones are
                // accepted (or answered with their own 409) and only the different one remains.
                foreach (var entry in entries)
                {
                    if (!await SendAsync(client, [entry], counts, cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                return true;

            case FailureKind.TooLarge when entries.Count == 1:
                await DeadLetterAsync(entries[0], OutboxDeadLetterReason.TooLarge, error, counts).ConfigureAwait(false);
                return true;

            case FailureKind.TooLarge:
                _batchSize = Math.Min(_batchSize, Math.Max(1, entries.Count / 2));
                return await SendHalvesAsync(client, entries, counts, cancellationToken).ConfigureAwait(false);

            case FailureKind.Rejected when entries.Count == 1:
                await DeadLetterAsync(entries[0], OutboxDeadLetterReason.Rejected, error, counts).ConfigureAwait(false);
                return true;

            default:
                // The LRS stores all of a batch or none, so the valid Statements are found by bisection:
                // about 2·log2(n) extra requests per invalid Statement
                return await SendHalvesAsync(client, entries, counts, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> SendHalvesAsync(IXapiClient client, IReadOnlyList<OutboxEntry> entries, FlushCounts counts, CancellationToken cancellationToken)
    {
        var middle = entries.Count / 2;
        return await SendAsync(client, entries.Take(middle).ToList(), counts, cancellationToken).ConfigureAwait(false)
            && await SendAsync(client, entries.Skip(middle).ToList(), counts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The LRS answered 409 to this Statement alone: delivered if what it stores is equivalent.</summary>
    /// <remarks>IEEE 9274.1.1, 4.1.6.1: an LRS that receives a different Statement with a known id answers 409.</remarks>
    private async Task<bool> ResolveConflictAsync(IXapiClient client, OutboxEntry entry, XapiHttpException conflict, FlushCounts counts, CancellationToken cancellationToken)
    {
        Statement? stored;
        try
        {
            // A Statement voided since it was stored is only returned as a voided Statement
            stored = await client.GetStatementAsync(entry.StatementId, StatementFormat.Exact, cancellationToken).ConfigureAwait(false)
                ?? await client.GetVoidedStatementAsync(entry.StatementId, StatementFormat.Exact, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Classified below
        catch (Exception exception)
#pragma warning restore CA1031
        {
            switch (FailureClassifier.Classify(exception))
            {
                case FailureKind.Transient:
                    await FailedAsync([entry], exception, counts).ConfigureAwait(false);
                    return false;
                case FailureKind.Pause:
                    Pause(exception);
                    return false;
                default:
                    await DeadLetterAsync(entry, OutboxDeadLetterReason.Conflict, conflict, counts, $"The LRS answered 409 Conflict, and fetching its Statement {entry.StatementId} to compare failed: {Describe(exception)}").ConfigureAwait(false);
                    return true;
            }
        }

        if (stored is not null && StatementEquivalence.AreEquivalent(entry.Statement, stored))
        {
            await DeliveredAsync([entry], counts).ConfigureAwait(false);
            return true;
        }

        var reason = stored is null
            ? $"The LRS answered 409 Conflict, but returns no Statement with id {entry.StatementId}."
            : $"The LRS answered 409 Conflict: it stores a different Statement with id {entry.StatementId}.";
        await DeadLetterAsync(entry, OutboxDeadLetterReason.Conflict, conflict, counts, reason).ConfigureAwait(false);
        return true;
    }

    // Bookkeeping after a request uses CancellationToken.None: the LRS has already acted, so the store must record it

    private async Task DeliveredAsync(IReadOnlyList<OutboxEntry> entries, FlushCounts counts)
    {
        var ids = entries.Select(e => e.StatementId).ToList();
        await _store.RemoveAsync(ids, CancellationToken.None).ConfigureAwait(false);
        counts.Delivered += ids.Count;
        Reachable();
        Raise(Delivered, new OutboxDeliveredEventArgs(ids));
    }

    private async Task DeadLetterAsync(OutboxEntry entry, OutboxDeadLetterReason reason, Exception error, FlushCounts counts, string? message = null)
    {
        var http = error as XapiHttpException;
        var deadLetter = new OutboxDeadLetter(entry.Statement, reason, message ?? Describe(error), _time.GetUtcNow())
        {
            StatusCode = http is null || http.StatusCode == 0 ? null : http.StatusCode,
            ResponseBody = Truncate(http?.ResponseBody),
            EnqueuedAt = entry.EnqueuedAt,
            Attempts = entry.Attempts,
        };
        await _store.MoveToDeadLettersAsync(deadLetter, CancellationToken.None).ConfigureAwait(false);
        counts.DeadLettered++;
        _metrics.DeadLettered(reason);
        if (http is not null && reason != OutboxDeadLetterReason.MaxAttemptsExceeded)
        {
            Reachable();
        }

        Raise(DeadLettered, new OutboxDeadLetteredEventArgs(deadLetter));
    }

    private async Task FailedAsync(IReadOnlyList<OutboxEntry> entries, Exception error, FlushCounts counts)
    {
        var message = Describe(error);
        await _store.RecordFailedAttemptAsync(entries.Select(e => e.StatementId).ToList(), message, CancellationToken.None).ConfigureAwait(false);
        var retried = entries.Count;
        if (_maxAttempts is { } maxAttempts)
        {
            foreach (var entry in entries.Where(e => e.Attempts + 1 >= maxAttempts))
            {
                retried--;
                await DeadLetterAsync(
                    entry with { Attempts = entry.Attempts + 1 },
                    OutboxDeadLetterReason.MaxAttemptsExceeded,
                    error,
                    counts,
                    $"Gave up after {entry.Attempts + 1} failed attempts (OutboxOptions.MaxAttempts). Last error: {message}").ConfigureAwait(false);
            }
        }

        _metrics.Retried(retried, error);
        ScheduleRetry(message, (error as XapiHttpException)?.RetryAfter);
    }

    private void ScheduleRetry(string error, TimeSpan? retryAfter)
    {
        lock (_stateGate)
        {
            _consecutiveFailures++;
            _lastError = error;
            // Never earlier than the LRS asked, and never a tight loop when it asks for 0
            var backoff = Backoff(_consecutiveFailures);
            _retryAt = _time.GetUtcNow() + (retryAfter > backoff ? retryAfter.Value : backoff);
        }
    }

    /// <summary>Equal jitter: a random delay between half of and all of the exponential delay. Holds <see cref="_stateGate"/>.</summary>
    private TimeSpan Backoff(int failures)
    {
        var exponential = _initialRetryDelay.Ticks * Math.Pow(2, Math.Min(failures - 1, 30));
        var ceiling = Math.Min(exponential, _maxRetryDelay.Ticks);
        return TimeSpan.FromTicks((long)(ceiling * (0.5 + (0.5 * _random.NextDouble()))));
    }

    /// <summary>The LRS answered for a Statement: whatever failed before is over.</summary>
    private void Reachable()
    {
        lock (_stateGate)
        {
            _consecutiveFailures = 0;
            _lastError = null;
            _retryAt = null;
        }
    }

    private void Pause(Exception error)
    {
        lock (_stateGate)
        {
            _paused = true;
            _lastError = Describe(error);
            _retryAt = null;
        }

        Raise(Paused, new OutboxPausedEventArgs(error));
    }

    private bool IsPaused
    {
        get
        {
            lock (_stateGate)
            {
                return _paused;
            }
        }
    }

    private void SetSending(bool sending)
    {
        lock (_stateGate)
        {
            _sending = sending;
        }
    }

    private static string Describe(Exception error) => $"{error.GetType().Name}: {error.Message}";

    private static string? Truncate(string? text) =>
        text is null || text.Length <= MaxResponseBodyLength ? text : text.Remove(MaxResponseBodyLength) + "…";

    private sealed class FlushCounts
    {
        public int Delivered { get; set; }

        public int DeadLettered { get; set; }
    }
}
