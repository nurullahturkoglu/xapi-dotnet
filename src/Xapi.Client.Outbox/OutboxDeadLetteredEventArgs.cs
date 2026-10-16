namespace Xapi.Client.Outbox;

/// <summary>Data for <see cref="StatementOutbox.DeadLettered"/>.</summary>
/// <param name="deadLetter">The dead letter.</param>
public sealed class OutboxDeadLetteredEventArgs(OutboxDeadLetter deadLetter) : EventArgs
{
    /// <summary>The dead letter.</summary>
    public OutboxDeadLetter DeadLetter { get; } = deadLetter;
}
