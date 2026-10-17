using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xapi.Client.Outbox.Tests.Fakes;
using Xapi.Testing;
using static Xapi.Client.Outbox.Tests.Fakes.Samples;

namespace Xapi.Client.Outbox.Tests.DependencyInjection;

public sealed class AddXapiOutboxTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Hosted_service_delivers_with_the_registered_client_and_file_store()
    {
        var lrs = new FakeLrs();
        var services = new ServiceCollection();
        services.AddXapiClient(o =>
        {
            o.Endpoint = new Uri(Harness.Endpoint);
            o.Version = XapiVersion.V2_0_0;
        }).HttpClient.ConfigurePrimaryHttpMessageHandler(() => lrs);
        services.AddXapiOutbox(_directory.Path, o => o.BatchSize = 7);
        services.AddXapiOutbox(_directory.Path);

        await using (var provider = services.BuildServiceProvider())
        {
            var outbox = provider.GetRequiredService<StatementOutbox>();
            Assert.IsType<FileOutboxStore>(provider.GetRequiredService<IOutboxStore>());
            var hosted = Assert.Single(provider.GetServices<IHostedService>());

            await hosted.StartAsync(Ct);
            var id = await outbox.EnqueueAsync(Statement(1), Ct);
            await Eventually(async () => (await outbox.GetStatusAsync(Ct)).PendingCount == 0, "the hosted service delivers the Statement");
            await hosted.StopAsync(Ct);
            Assert.Contains(id, lrs.StoredIds);
        }

        // Disposing the provider released the store's directory lock
        using var reopened = new FileOutboxStore(_directory.Path);
        Assert.Equal(0, await reopened.GetPendingCountAsync(Ct));
    }

    [Fact]
    public void Custom_store_factory_is_used()
    {
        var store = new InMemoryOutboxStore();
        var services = new ServiceCollection();
        services.AddXapiClient(o => o.Endpoint = new Uri(Harness.Endpoint));
        services.AddXapiOutbox(_ => store);

        using var provider = services.BuildServiceProvider();

        Assert.Same(store, provider.GetRequiredService<IOutboxStore>());
        Assert.NotNull(provider.GetRequiredService<StatementOutbox>());
    }

    [Fact]
    public async Task Outbox_reports_on_a_meter_from_the_hosts_meter_factory()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddXapiClient(o => o.Endpoint = new Uri(Harness.Endpoint));
        services.AddXapiOutbox(_ => new InMemoryOutboxStore());
        using var provider = services.BuildServiceProvider();
        var meterFactory = provider.GetRequiredService<IMeterFactory>();
        using var metrics = new MetricRecorder(i => ReferenceEquals(i.Meter.Scope, meterFactory));
        var outbox = provider.GetRequiredService<StatementOutbox>();

        await outbox.EnqueueAsync(Statement(1), Ct);
        await outbox.EnqueueAsync(Statement(2), Ct);

        var pending = Assert.Single(metrics.Observe(OutboxDiagnostics.StatementsPending));
        Assert.Equal(XapiClientDiagnostics.MeterName, pending.Meter);
        Assert.Equal(2, pending.Value);
    }
}
