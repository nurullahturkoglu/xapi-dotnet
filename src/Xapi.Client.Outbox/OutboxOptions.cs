namespace Xapi.Client.Outbox;

/// <summary>Settings for <see cref="StatementOutbox"/>. The outbox copies them when it is created.</summary>
public sealed class OutboxOptions
{
    /// <summary>The largest number of Statements sent in one request. Defaults to 50.</summary>
    /// <remarks>
    /// The outbox lowers it on its own, for its lifetime, when the LRS answers 413 Content Too Large.
    /// </remarks>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// The largest number of Statements that may wait in the outbox, or <see langword="null"/> for no limit.
    /// Defaults to 100,000. When it is reached, <see cref="StatementOutbox.EnqueueAsync"/> throws
    /// <see cref="OutboxFullException"/>; nothing already queued is dropped. Dead letters do not count.
    /// </summary>
    public int? MaxQueueSize { get; set; } = 100_000;

    /// <summary>The delay after the first transient failure. Defaults to 2 seconds.</summary>
    /// <remarks>
    /// After the n-th failure in a row the outbox waits a random time between half of and all of
    /// <c>InitialRetryDelay × 2^(n−1)</c>, at most <see cref="MaxRetryDelay"/>. When the LRS sends a longer
    /// <c>Retry-After</c>, the outbox waits that long instead, even beyond <see cref="MaxRetryDelay"/>.
    /// </remarks>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest delay between retries, before jitter. Defaults to 5 minutes.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many transient failures a Statement may have before it is moved to the dead letters with
    /// <see cref="OutboxDeadLetterReason.MaxAttemptsExceeded"/>, or <see langword="null"/> (the default) to retry
    /// transient errors for ever.
    /// </summary>
    /// <remarks>
    /// Only the Statements of the request that failed are counted, so during a long outage the oldest
    /// Statements run out of attempts first. Leave it unset for offline use.
    /// </remarks>
    public int? MaxAttempts { get; set; }

    /// <summary>The clock and timers the outbox uses. Defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
