using System.Diagnostics.Metrics;

namespace Xapi.Client.Internal;

// The client's instruments on the Xapi.Client meter (ADR-014). A client created with its public constructor uses
// a meter shared by the process; AddXapiClient creates one through the host's IMeterFactory.
internal sealed class ClientMetrics
{
    private readonly Counter<long> _statementsSent;

    public ClientMetrics(Meter meter)
    {
        _statementsSent = meter.CreateCounter<long>(
            XapiClientDiagnostics.StatementsSent,
            unit: "{statement}",
            description: "Statements the LRS accepted from the client.");
    }

    public static ClientMetrics Shared { get; } = new(new Meter(XapiClientDiagnostics.MeterName));

    public void StatementsSent(int count, XapiVersion version)
    {
        if (_statementsSent.Enabled)
        {
            _statementsSent.Add(count, new KeyValuePair<string, object?>("xapi.version", version.HeaderValue));
        }
    }
}
