namespace Xapi.Client.Outbox;

/// <summary>What an outbox is doing.</summary>
public enum OutboxState
{
    /// <summary>Not sending, and nothing stops the next send.</summary>
    Idle,

    /// <summary>A flush is in progress.</summary>
    Sending,

    /// <summary>
    /// The last send failed with a transient error (network, timeout, 408, 429, 5xx); the background loop sends
    /// again at <see cref="OutboxStatus.RetryAt"/>.
    /// </summary>
    WaitingToRetry,

    /// <summary>
    /// The LRS refused the request itself (401, 403, or a status that means the endpoint is wrong, such as 404).
    /// Nothing is sent until <see cref="StatementOutbox.Resume"/> is called or a new outbox is created.
    /// </summary>
    Paused,
}
