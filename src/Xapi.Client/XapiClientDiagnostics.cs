namespace Xapi.Client;

/// <summary>
/// The names of the client's metrics, for OpenTelemetry's <c>AddMeter</c> or a
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>. The outbox in <c>Xapi.Client.Outbox</c> reports on the
/// same meter. The names are part of the public contract: they change only in a major release.
/// </summary>
/// <remarks>docs/client.md lists the tags of each instrument.</remarks>
public static class XapiClientDiagnostics
{
    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> of the client and the outbox: <c>Xapi.Client</c>.</summary>
    public const string MeterName = "Xapi.Client";

    /// <summary>
    /// Counter of the Statements the LRS accepted from <see cref="XapiClient.SaveStatementAsync"/> and
    /// <see cref="XapiClient.SaveStatementsAsync"/> (unit <c>{statement}</c>).
    /// </summary>
    public const string StatementsSent = "xapi.client.statements.sent";
}
