namespace Xapi.Client.Outbox;

/// <summary>Data for <see cref="StatementOutbox.Delivered"/>.</summary>
/// <param name="statementIds">The ids of the delivered Statements, in the order they were queued.</param>
public sealed class OutboxDeliveredEventArgs(IReadOnlyList<Guid> statementIds) : EventArgs
{
    /// <summary>The ids of the delivered Statements, in the order they were queued.</summary>
    public IReadOnlyList<Guid> StatementIds { get; } = statementIds;
}
