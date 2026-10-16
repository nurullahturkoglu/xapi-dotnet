using Xapi.Client.Outbox.Internal;

namespace Xapi.Client.Outbox;

/// <summary>A Statement waiting in an outbox, with its delivery history.</summary>
public sealed record OutboxEntry
{
    /// <summary>Creates an entry.</summary>
    /// <param name="statement">The Statement. It must have an id, which identifies the entry.</param>
    /// <param name="enqueuedAt">When the Statement was added to the outbox.</param>
    /// <exception cref="ArgumentException"><paramref name="statement"/> has no id.</exception>
    public OutboxEntry(Statement statement, DateTimeOffset enqueuedAt)
    {
        Statement = Guard.WithId(statement);
        EnqueuedAt = enqueuedAt;
    }

    /// <summary>The Statement id, which identifies the entry in the store.</summary>
    public Guid StatementId => Statement.Id!.Value;

    /// <summary>The Statement, exactly as it will be sent.</summary>
    public Statement Statement { get; }

    /// <summary>When the Statement was added to the outbox.</summary>
    public DateTimeOffset EnqueuedAt { get; }

    /// <summary>How many sends of this Statement failed with a transient error.</summary>
    public int Attempts { get; init; }

    /// <summary>The error of the last failed send, if any.</summary>
    public string? LastError { get; init; }
}
