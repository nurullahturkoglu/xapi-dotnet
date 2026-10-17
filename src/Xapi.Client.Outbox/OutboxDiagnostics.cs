namespace Xapi.Client.Outbox;

/// <summary>
/// The names of the outbox's metrics. They are reported on the client's meter,
/// <see cref="XapiClientDiagnostics.MeterName"/> (<c>Xapi.Client</c>), next to
/// <see cref="XapiClientDiagnostics.StatementsSent"/>, which counts what the outbox delivers. The names are part of
/// the public contract: they change only in a major release.
/// </summary>
/// <remarks>docs/outbox.md lists the tags of each instrument.</remarks>
public static class OutboxDiagnostics
{
    /// <summary>
    /// Counter of the Statements whose send failed with a transient error and that stay queued to be sent again
    /// (unit <c>{statement}</c>).
    /// </summary>
    public const string StatementsRetried = "xapi.client.outbox.statements.retried";

    /// <summary>Counter of the Statements moved to the dead letters (unit <c>{statement}</c>).</summary>
    public const string StatementsDeadLettered = "xapi.client.outbox.statements.dead_lettered";

    /// <summary>
    /// Observable up-down counter of the Statements waiting to be delivered, summed over the outboxes that report
    /// on the meter (unit <c>{statement}</c>). Dead letters are not included.
    /// </summary>
    public const string StatementsPending = "xapi.client.outbox.statements.pending";
}
