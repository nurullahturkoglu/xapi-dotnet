using Microsoft.Extensions.Time.Testing;
using Xapi.Client.Outbox.Internal;

namespace Xapi.Client.Outbox.Tests.Fakes;

/// <summary>An outbox wired to a <see cref="FakeLrs"/> through a real <see cref="XapiClient"/>, on a fake clock.</summary>
internal sealed class Harness : IAsyncDisposable
{
    public const string Endpoint = "https://lrs.example.com/xapi/";

    public static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private Harness(FakeLrs lrs, OutboxStore store, FakeTimeProvider time, StatementOutbox outbox, XapiClient client)
    {
        Lrs = lrs;
        Store = store;
        Time = time;
        Outbox = outbox;
        Client = client;
    }

    public FakeLrs Lrs { get; }

    public OutboxStore Store { get; }

    public FakeTimeProvider Time { get; }

    public StatementOutbox Outbox { get; }

    public XapiClient Client { get; }

    public static Harness Create(Action<OutboxOptions>? configure = null, OutboxStore? store = null, FakeLrs? lrs = null, XapiVersion? version = null, OutboxMetrics? metrics = null)
    {
        lrs ??= new FakeLrs();
        var time = new FakeTimeProvider(Start);
        var client = new XapiClient(new HttpClient(lrs), new XapiClientOptions
        {
            Endpoint = new Uri(Endpoint),
            Version = version ?? XapiVersion.V2_0_0,
            Credentials = XapiCredentials.Basic("key", "secret"),
        });
        var options = new OutboxOptions { TimeProvider = time };
        configure?.Invoke(options);
        store ??= new InMemoryOutboxStore();
        var outbox = metrics is null ? new StatementOutbox(client, store, options) : new StatementOutbox(() => client, store, options, metrics);
        return new Harness(lrs, store, time, outbox, client);
    }

    public async Task<List<Guid>> EnqueueAsync(int count, int first = 1)
    {
        var ids = new List<Guid>();
        for (var i = first; i < first + count; i++)
        {
            ids.Add(await Outbox.EnqueueAsync(Samples.Statement(i), TestContext.Current.CancellationToken));
        }

        return ids;
    }

    public ValueTask DisposeAsync() => Outbox.DisposeAsync();
}
