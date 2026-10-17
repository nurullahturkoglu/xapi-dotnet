using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using Xapi.Testing;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests.DependencyInjection;

public class AddXapiClientTests
{
    private static ServiceProvider Build(FakeHandler handler, Action<XapiClientOptions>? configure = null, Action<IXapiClientBuilder>? builder = null)
    {
        var services = new ServiceCollection();
        var xapi = services.AddXapiClient(o =>
        {
            o.Endpoint = new Uri(TestClient.Endpoint);
            o.Credentials = XapiCredentials.Basic("key", "secret");
            configure?.Invoke(o);
        });
        xapi.HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        builder?.Invoke(xapi);
        Assert.Same(services, xapi.Services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Resolved_client_uses_the_configured_options_and_handler()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.NotFound);
        using var provider = Build(handler, o => o.Version = XapiVersion.V1_0_3);

        var client = provider.GetRequiredService<IXapiClient>();
        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.IsType<XapiClient>(client);
        var request = handler.Single;
        Assert.Equal(TestClient.Endpoint + "activities", request.Path);
        Assert.Equal("1.0.3", request.Header("X-Experience-API-Version"));
        Assert.Equal("Basic a2V5OnNlY3JldA==", request.Header("Authorization"));
    }

    [Fact]
    public async Task Negotiated_version_is_shared_by_every_resolved_client()
    {
        var handler = new FakeHandler();
        handler.RespondJson("{\"version\":[\"1.0.3\"]}");
        handler.Fallback = _ => FakeHandler.Response(HttpStatusCode.NotFound);
        using var provider = Build(handler);

        var first = provider.GetRequiredService<IXapiClient>();
        var second = provider.GetRequiredService<IXapiClient>();
        await first.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);
        await second.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.NotSame(first, second);
        Assert.Equal(
            ["about", "activities", "activities"],
            handler.Requests.Select(r => r.Path.Substring(TestClient.Endpoint.Length)));
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("1.0.3", r.Header("X-Experience-API-Version")));
    }

    [Fact]
    public async Task Resilience_handler_can_be_added_through_the_http_client_builder()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.ServiceUnavailable);
        handler.Respond(HttpStatusCode.NotFound);
        using var provider = Build(handler, o => o.Version = XapiVersion.V2_0_0, xapi => xapi.HttpClient.AddStandardResilienceHandler(o =>
        {
            o.Retry.Delay = TimeSpan.Zero;
            o.Retry.DisableForUnsafeHttpMethods();
        }));

        var activity = await provider.GetRequiredService<IXapiClient>().GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.Null(activity);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Resolved_clients_report_on_a_meter_from_the_hosts_meter_factory()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.NoContent);
        handler.Respond(HttpStatusCode.NoContent);
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddXapiClient(o =>
        {
            o.Endpoint = new Uri(TestClient.Endpoint);
            o.Version = XapiVersion.V2_0_0;
        }).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var metrics = new MetricRecorder(i => ReferenceEquals(i.Meter.Scope, meterFactory));

        await provider.GetRequiredService<IXapiClient>().SaveStatementAsync(Samples.Statement(Samples.Id(1)), cancellationToken: Ct);
        await provider.GetRequiredService<IXapiClient>().SaveStatementAsync(Samples.Statement(Samples.Id(2)), cancellationToken: Ct);

        var sent = metrics.Of(XapiClientDiagnostics.StatementsSent);
        Assert.Equal(2, sent.Count);
        Assert.All(sent, m => Assert.Equal(XapiClientDiagnostics.MeterName, m.Meter));
    }
}
