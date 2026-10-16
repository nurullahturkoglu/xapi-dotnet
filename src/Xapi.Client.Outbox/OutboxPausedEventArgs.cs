namespace Xapi.Client.Outbox;

/// <summary>Data for <see cref="StatementOutbox.Paused"/>.</summary>
/// <param name="error">The error that paused the outbox, usually an <see cref="XapiHttpException"/> with 401 or 403.</param>
public sealed class OutboxPausedEventArgs(Exception error) : EventArgs
{
    /// <summary>The error that paused the outbox, usually an <see cref="XapiHttpException"/> with 401 or 403.</summary>
    public Exception Error { get; } = error;
}
