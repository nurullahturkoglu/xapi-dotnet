using System.Diagnostics.Metrics;
using System.Net;
using Xapi.Client.Internal;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using Xapi.Testing;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class MetricsTests
{
    [Theory]
    [InlineData("1.0.3")]
    [InlineData("2.0.0")]
    public async Task Statements_the_lrs_accepted_are_counted_with_the_xapi_version(string versionText)
    {
        Assert.True(XapiVersion.TryParseHeader(versionText, out var version));
        using var meter = new Meter(XapiClientDiagnostics.MeterName);
        using var metrics = new MetricRecorder(i => i.Meter == meter);
        var (client, handler) = TestClient.Create(version, metrics: new ClientMetrics(meter));
        handler.Respond(HttpStatusCode.NoContent);
        handler.RespondJson($"[\"{Samples.Id(2)}\"]");
        handler.RespondJson($"[\"{Samples.Id(3)}\",\"{Samples.Id(4)}\",\"{Samples.Id(5)}\"]");
        handler.Respond(HttpStatusCode.BadRequest, "{}");
        handler.Respond(HttpStatusCode.ServiceUnavailable);

        await client.SaveStatementAsync(Samples.Statement(Samples.Id(1)), cancellationToken: Ct);
        await client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct);
        await client.SaveStatementsAsync([Samples.Statement(), Samples.Statement(), Samples.Statement()], cancellationToken: Ct);
        await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));
        await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementsAsync([Samples.Statement(), Samples.Statement()], cancellationToken: Ct));

        Assert.Equal(new Dictionary<string, double> { ["xapi.version=" + versionText] = 5 }, metrics.Sums(XapiClientDiagnostics.StatementsSent));
        Assert.All(metrics.Of(XapiClientDiagnostics.StatementsSent), m => Assert.Equal("{statement}", m.Unit));
    }

    [Fact]
    public async Task Client_created_with_the_public_constructor_reports_on_the_shared_meter()
    {
        // The shared meter is process-wide: other tests' clients report on it too, so only this handler's count is
        // compared, as a lower bound
        using var metrics = new MetricRecorder(i => i.Meter.Name == XapiClientDiagnostics.MeterName && MetricRecorder.IsUnscoped(i.Meter));
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);

        await client.SaveStatementAsync(Samples.Statement(Samples.Id(1)), cancellationToken: Ct);

        Assert.Contains(metrics.Of(XapiClientDiagnostics.StatementsSent), m => m.Value == 1 && m.TagText == "xapi.version=2.0.0");
    }
}
