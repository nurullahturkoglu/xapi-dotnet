namespace Xapi.Client.Outbox;

public sealed partial class StatementOutbox
{
    private TaskCompletionSource<bool> _wake = NewWake();
    private Task? _loop;

    /// <summary>
    /// Delivers Statements in the background until <paramref name="cancellationToken"/> is cancelled or the outbox
    /// is disposed: right away, whenever a Statement is enqueued, and at <see cref="OutboxStatus.RetryAt"/> after
    /// a transient failure. While the outbox is paused it waits for <see cref="Resume"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needs no host or dependency injection, so it also suits Unity and desktop apps: start it once
    /// (<c>_ = outbox.RunAsync(token)</c>) and cancel the token, or dispose the outbox, on exit. On net10.0,
    /// <c>AddOutbox</c> runs it as a hosted service. <see cref="FlushAsync"/> can still be called while it runs.
    /// </para>
    /// <para>
    /// Errors never end the loop. Failures of the store (a full disk, for example) are handled like transient
    /// failures and shown in <see cref="OutboxStatus.LastError"/>.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Stops the loop. A request in flight is abandoned; its Statements stay queued.</param>
    /// <returns>A task that completes, without an exception, when the loop has stopped.</returns>
    /// <exception cref="InvalidOperationException">The loop is already running.</exception>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var loop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _loop, loop.Task, null) is not null)
        {
            throw new InvalidOperationException("The outbox's background loop is already running.");
        }

        return RunLoopAsync(loop, cancellationToken);
    }

    private async Task RunLoopAsync(TaskCompletionSource<bool> loop, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
        var token = stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Taken before the flush, so a Statement enqueued during it wakes the wait below at once
                var wake = Volatile.Read(ref _wake).Task;
                if (CanSendNow())
                {
                    await BackgroundFlushAsync(token).ConfigureAwait(false);
                }

                await WaitAsync(wake, DelayUntilRetry(), token).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _loop, null);
            loop.SetResult(true);
        }
    }

    private async Task BackgroundFlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _flushGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // The loop must outlive store failures; they are reported through the status
        catch (Exception exception)
#pragma warning restore CA1031
        {
            ScheduleRetry(Describe(exception), null);
        }
    }

    private async Task WaitAsync(Task wake, TimeSpan? delay, CancellationToken cancellationToken)
    {
        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timer = delay is { } due
#if NET
            ? Task.Delay(due, _time, stopWaiting.Token)
#else
            ? _time.Delay(due, stopWaiting.Token)
#endif
            : Task.Delay(Timeout.Infinite, stopWaiting.Token);
        await Task.WhenAny(wake, timer).ConfigureAwait(false);

        // Releases the timer when the wake signal came first
        stopWaiting.Cancel();
    }

    private bool CanSendNow()
    {
        lock (_stateGate)
        {
            return !_paused && (_retryAt is not { } retryAt || retryAt <= _time.GetUtcNow());
        }
    }

    /// <summary>The time until the next retry, or <see langword="null"/> to wait for a wake signal only.</summary>
    private TimeSpan? DelayUntilRetry()
    {
        lock (_stateGate)
        {
            if (_paused || _retryAt is not { } retryAt)
            {
                return null;
            }

            var delay = retryAt - _time.GetUtcNow();
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
    }

    private void Wake() => Interlocked.Exchange(ref _wake, NewWake()).TrySetResult(true);

    private static TaskCompletionSource<bool> NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
