namespace Xapi.Client.Outbox;

/// <summary>
/// Thrown by <see cref="StatementOutbox.EnqueueAsync"/> when <see cref="OutboxOptions.MaxQueueSize"/> Statements
/// are already waiting. The Statement was not stored.
/// </summary>
public sealed class OutboxFullException : InvalidOperationException
{
    /// <summary>Creates an exception with a generic message.</summary>
    public OutboxFullException()
        : this("The outbox is full.")
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public OutboxFullException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public OutboxFullException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
