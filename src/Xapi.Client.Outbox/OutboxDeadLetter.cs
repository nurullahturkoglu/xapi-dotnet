using System.Net;
using Xapi.Client.Outbox.Internal;

namespace Xapi.Client.Outbox;

/// <summary>
/// A Statement the outbox gave up on, with the reason. Dead letters stay in the store until they are requeued
/// (<see cref="StatementOutbox.RequeueDeadLetterAsync"/>) or removed (<see cref="StatementOutbox.RemoveDeadLetterAsync"/>).
/// </summary>
public sealed record OutboxDeadLetter
{
    /// <summary>Creates a dead letter.</summary>
    /// <param name="statement">The Statement. It must have an id.</param>
    /// <param name="reason">Why it was not delivered.</param>
    /// <param name="error">A description of the error.</param>
    /// <param name="deadLetteredAt">When it was moved to the dead letters.</param>
    /// <exception cref="ArgumentException"><paramref name="statement"/> has no id.</exception>
    public OutboxDeadLetter(Statement statement, OutboxDeadLetterReason reason, string error, DateTimeOffset deadLetteredAt)
    {
        Statement = Guard.WithId(statement);
        Reason = reason;
        Error = Guard.NotNull(error);
        DeadLetteredAt = deadLetteredAt;
    }

    /// <summary>The Statement id.</summary>
    public Guid StatementId => Statement.Id!.Value;

    /// <summary>The Statement.</summary>
    public Statement Statement { get; }

    /// <summary>Why it was not delivered.</summary>
    public OutboxDeadLetterReason Reason { get; }

    /// <summary>A description of the error, for example the exception message with the LRS's status code.</summary>
    public string Error { get; }

    /// <summary>The HTTP status code the LRS answered with, or <see langword="null"/> when no response decided it.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>The LRS's response body, which usually says what is wrong with the Statement. Long bodies are cut.</summary>
    public string? ResponseBody { get; init; }

    /// <summary>When the Statement was first added to the outbox.</summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>When it was moved to the dead letters.</summary>
    public DateTimeOffset DeadLetteredAt { get; }

    /// <summary>How many sends of it had failed with a transient error before.</summary>
    public int Attempts { get; init; }
}
