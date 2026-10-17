using Xapi.Client.Outbox.Internal;

namespace Xapi.Client.Outbox;

/// <summary>
/// An <see cref="OutboxStore"/> that keeps everything in memory. Queued Statements are lost when the process
/// ends, so use it for tests, or where that is acceptable; use <see cref="FileOutboxStore"/> otherwise.
/// </summary>
/// <remarks>Thread-safe.</remarks>
public sealed class InMemoryOutboxStore : OutboxStore
{
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly OutboxQueue _queue = new();

    /// <inheritdoc/>
    public override ValueTask<bool> AddAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        Guard.NotNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.TryAdd(entry));
        }
    }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OutboxEntry>> GetPendingAsync(int maxCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.Peek(maxCount));
        }
    }

    /// <inheritdoc/>
    public override ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.PendingCount);
        }
    }

    /// <inheritdoc/>
    public override ValueTask RemoveAsync(IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken)
    {
        Guard.NotNull(statementIds);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var id in statementIds)
            {
                _queue.Remove(id);
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public override ValueTask RecordFailedAttemptAsync(IReadOnlyCollection<Guid> statementIds, string error, CancellationToken cancellationToken)
    {
        Guard.NotNull(statementIds);
        Guard.NotNull(error);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var id in statementIds)
            {
                _queue.RecordFailedAttempt(id, error);
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public override ValueTask MoveToDeadLettersAsync(OutboxDeadLetter deadLetter, CancellationToken cancellationToken)
    {
        Guard.NotNull(deadLetter);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _queue.MoveToDeadLetters(deadLetter);
        }

        return default;
    }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OutboxDeadLetter>> GetDeadLettersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.DeadLetters.ToList());
        }
    }

    /// <inheritdoc/>
    public override ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.DeadLetterCount);
        }
    }

    /// <inheritdoc/>
    public override ValueTask<bool> RequeueAsync(Guid statementId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.Requeue(statementId));
        }
    }

    /// <inheritdoc/>
    public override ValueTask<bool> RemoveDeadLetterAsync(Guid statementId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new(_queue.RemoveDeadLetter(statementId));
        }
    }
}
