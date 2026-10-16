namespace Xapi.Client.Outbox;

/// <summary>A snapshot of an outbox: what it is doing, how much is waiting, and the last error.</summary>
public sealed record OutboxStatus
{
    /// <summary>What the outbox is doing.</summary>
    public OutboxState State { get; init; }

    /// <summary>The number of Statements waiting to be delivered.</summary>
    public int PendingCount { get; init; }

    /// <summary>The number of dead letters.</summary>
    public int DeadLetterCount { get; init; }

    /// <summary>
    /// The error that made the last send fail or the outbox pause, or <see langword="null"/> when the last send
    /// succeeded.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>The number of sends in a row that failed with a transient error. Reset by a successful send.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>When the background loop sends again, while <see cref="State"/> is <see cref="OutboxState.WaitingToRetry"/>.</summary>
    public DateTimeOffset? RetryAt { get; init; }
}
