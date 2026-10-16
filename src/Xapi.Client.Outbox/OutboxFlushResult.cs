namespace Xapi.Client.Outbox;

/// <summary>What one <see cref="StatementOutbox.FlushAsync"/> did.</summary>
public sealed record OutboxFlushResult
{
    /// <summary>The number of Statements delivered (or found already stored and equivalent at the LRS).</summary>
    public int Delivered { get; init; }

    /// <summary>The number of Statements moved to the dead letters.</summary>
    public int DeadLettered { get; init; }

    /// <summary>
    /// The outbox after the flush. <see cref="OutboxStatus.State"/> says why it stopped:
    /// <see cref="OutboxState.Idle"/> when the queue was drained, <see cref="OutboxState.WaitingToRetry"/> after a
    /// transient error, <see cref="OutboxState.Paused"/> when the LRS refused the request.
    /// </summary>
    public OutboxStatus Status { get; init; } = new();
}
