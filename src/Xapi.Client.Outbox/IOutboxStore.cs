namespace Xapi.Client.Outbox;

/// <summary>
/// Where an outbox keeps its Statements: an ordered queue of pending entries, keyed by Statement id, and a set of
/// dead letters.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be safe to call from several threads at once: <see cref="StatementOutbox.EnqueueAsync"/>
/// adds entries while a flush reads and removes others. Every method that changes the store must have made the
/// change durable when its task completes, because the outbox reports success to its callers at that point.
/// </para>
/// <para>
/// Built in: <see cref="FileOutboxStore"/> (durable, for applications) and <see cref="InMemoryOutboxStore"/>
/// (for tests, and for cases where losing queued Statements on exit is acceptable).
/// </para>
/// </remarks>
public interface IOutboxStore
{
    /// <summary>Adds an entry at the end of the queue, unless an entry with the same Statement id is pending.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the entry was added; <see langword="false"/> when its id was already pending.</returns>
    ValueTask<bool> AddAsync(OutboxEntry entry, CancellationToken cancellationToken);

    /// <summary>Returns the oldest pending entries, in queue order, without removing them.</summary>
    /// <param name="maxCount">The largest number of entries to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Up to <paramref name="maxCount"/> entries; empty when nothing is pending.</returns>
    ValueTask<IReadOnlyList<OutboxEntry>> GetPendingAsync(int maxCount, CancellationToken cancellationToken);

    /// <summary>Returns the number of pending entries.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of pending entries.</returns>
    ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken);

    /// <summary>Removes delivered entries. Ids that are not pending are ignored.</summary>
    /// <param name="statementIds">The Statement ids.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the removal is durable.</returns>
    ValueTask RemoveAsync(IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed send of pending entries: increments <see cref="OutboxEntry.Attempts"/> and sets
    /// <see cref="OutboxEntry.LastError"/>. Ids that are not pending are ignored.
    /// </summary>
    /// <param name="statementIds">The Statement ids.</param>
    /// <param name="error">A description of the error.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is durable.</returns>
    ValueTask RecordFailedAttemptAsync(IReadOnlyCollection<Guid> statementIds, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the pending entry with the dead letter's Statement id, if there is one, and stores the dead letter,
    /// replacing any dead letter with the same id.
    /// </summary>
    /// <param name="deadLetter">The dead letter.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is durable.</returns>
    ValueTask MoveToDeadLettersAsync(OutboxDeadLetter deadLetter, CancellationToken cancellationToken);

    /// <summary>Returns the dead letters, oldest first.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The dead letters.</returns>
    ValueTask<IReadOnlyList<OutboxDeadLetter>> GetDeadLettersAsync(CancellationToken cancellationToken);

    /// <summary>Returns the number of dead letters.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of dead letters.</returns>
    ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves a dead letter back to the end of the queue as a pending entry with no failed attempts. If an entry
    /// with the same id is already pending, the dead letter is only removed.
    /// </summary>
    /// <param name="statementId">The Statement id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="false"/> when there is no dead letter with that id.</returns>
    ValueTask<bool> RequeueAsync(Guid statementId, CancellationToken cancellationToken);

    /// <summary>Deletes a dead letter.</summary>
    /// <param name="statementId">The Statement id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="false"/> when there is no dead letter with that id.</returns>
    ValueTask<bool> RemoveDeadLetterAsync(Guid statementId, CancellationToken cancellationToken);
}
