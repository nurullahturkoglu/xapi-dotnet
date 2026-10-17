using System.Net;
using Xapi.Client.Outbox;

namespace Xapi.Client.IntegrationTests;

/// <summary>StatementOutbox against lrsql: offline queueing, idempotent resend and 409 resolution with a real LRS.</summary>
public sealed class OutboxTests(LrsqlFixture lrs) : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xapi-outbox-integration", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Statements_queued_offline_are_delivered_in_order_once_online(string version)
    {
        var network = new Network { Offline = true };
        using var http = new HttpClient(network);
        var client = lrs.CreateClient(Data.Parse(version), http);
        var registration = Guid.NewGuid();
        var ids = new List<Guid>();

        using (var store = await FileOutboxStore.OpenAsync(_directory, Ct))
        {
            await using var outbox = new StatementOutbox(client, store, new OutboxOptions { BatchSize = 2 });
            for (var i = 0; i < 5; i++)
            {
                ids.Add(await outbox.EnqueueAsync(Data.NewStatement(registration: registration), Ct));
            }

            var offline = await outbox.FlushAsync(Ct);
            Assert.Equal(OutboxState.WaitingToRetry, offline.Status.State);
            Assert.Equal(5, offline.Status.PendingCount);
        }

        // As after an app restart: a new store and outbox on the same directory
        network.Offline = false;
        using (var store = await FileOutboxStore.OpenAsync(_directory, Ct))
        {
            await using var outbox = new StatementOutbox(client, store, new OutboxOptions { BatchSize = 2 });
            var online = await outbox.FlushAsync(Ct);
            Assert.Equal(5, online.Delivered);
            Assert.Equal(OutboxState.Idle, online.Status.State);
        }

        var stored = await client.QueryStatementsAsync(new StatementQuery { Registration = registration, Ascending = true }, Ct);
        Assert.Equal(ids, stored.Statements.Select(s => s.Id!.Value));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Batch_resent_after_a_lost_response_is_stored_once(string version)
    {
        var network = new Network { LoseNextResponse = true };
        using var http = new HttpClient(network);
        var client = lrs.CreateClient(Data.Parse(version), http);
        var registration = Guid.NewGuid();
        await using var outbox = new StatementOutbox(client, new InMemoryOutboxStore());
        for (var i = 0; i < 3; i++)
        {
            await outbox.EnqueueAsync(Data.NewStatement(registration: registration), Ct);
        }

        var lost = await outbox.FlushAsync(Ct);
        var resent = await outbox.FlushAsync(Ct);

        Assert.Equal(OutboxState.WaitingToRetry, lost.Status.State);
        Assert.Equal(3, resent.Delivered);
        Assert.Equal(0, resent.DeadLettered);
        var stored = await client.QueryStatementsAsync(new StatementQuery { Registration = registration }, Ct);
        Assert.Equal(3, stored.Statements.Count);
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Statement_already_stored_in_an_equivalent_form_counts_as_delivered(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var existing = Data.NewStatement() with { Id = UuidV7Generator.Shared.NewGuid() };
        await client.SaveStatementAsync(existing, cancellationToken: Ct);
        await using var outbox = new StatementOutbox(client, new InMemoryOutboxStore());

        // Equivalent but not equal: another Verb display and timestamp. Batched with a new Statement, so that a
        // conflict, if lrsql reports one, goes through the one-at-a-time fallback and the GET comparison.
        var resent = existing with
        {
            Verb = Verbs.Completed with { Display = LanguageMap.Create("de-DE", "abgeschlossen") },
            Timestamp = existing.Timestamp!.Value.AddHours(1),
        };
        await outbox.EnqueueAsync(resent, Ct);
        var fresh = await outbox.EnqueueAsync(Data.NewStatement(), Ct);
        var result = await outbox.FlushAsync(Ct);

        Assert.Equal(2, result.Delivered);
        Assert.Empty(await outbox.GetDeadLettersAsync(Ct));
        Assert.NotNull(await client.GetStatementAsync(fresh, cancellationToken: Ct));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Different_statement_with_a_stored_id_is_dead_lettered_and_the_rest_delivered(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var existing = Data.NewStatement() with { Id = UuidV7Generator.Shared.NewGuid() };
        await client.SaveStatementAsync(existing, cancellationToken: Ct);
        await using var outbox = new StatementOutbox(client, new InMemoryOutboxStore());

        var before = await outbox.EnqueueAsync(Data.NewStatement(), Ct);
        await outbox.EnqueueAsync(existing with { Verb = Verbs.Failed }, Ct);
        var after = await outbox.EnqueueAsync(Data.NewStatement(), Ct);
        var result = await outbox.FlushAsync(Ct);

        Assert.Equal(2, result.Delivered);
        var deadLetter = Assert.Single(await outbox.GetDeadLettersAsync(Ct));
        Assert.Equal(existing.Id, deadLetter.StatementId);
        Assert.Equal(OutboxDeadLetterReason.Conflict, deadLetter.Reason);
        Assert.Equal(HttpStatusCode.Conflict, deadLetter.StatusCode);
        Assert.NotNull(await client.GetStatementAsync(before, cancellationToken: Ct));
        Assert.NotNull(await client.GetStatementAsync(after, cancellationToken: Ct));
        Assert.Equal(Verbs.Completed.Id, (await client.GetStatementAsync(existing.Id!.Value, cancellationToken: Ct))?.Verb.Id);
    }

    [Fact]
    public async Task Statement_using_2_0_properties_is_dead_lettered_by_a_1_0_3_client()
    {
        var client = lrs.CreateClient(XapiVersion.V1_0_3);
        await using var outbox = new StatementOutbox(client, new InMemoryOutboxStore());
        var first = await outbox.EnqueueAsync(Data.NewStatement(), Ct);
        var twoOnly = await outbox.EnqueueAsync(
            Data.NewStatement() with { Context = new Context { ContextAgents = XapiList.Create(new ContextAgent(Data.NewAgent())) } }, Ct);
        var last = await outbox.EnqueueAsync(Data.NewStatement(), Ct);

        var result = await outbox.FlushAsync(Ct);

        Assert.Equal(2, result.Delivered);
        var deadLetter = Assert.Single(await outbox.GetDeadLettersAsync(Ct));
        Assert.Equal((twoOnly, OutboxDeadLetterReason.Rejected), (deadLetter.StatementId, deadLetter.Reason));
        Assert.NotNull(await client.GetStatementAsync(first, cancellationToken: Ct));
        Assert.NotNull(await client.GetStatementAsync(last, cancellationToken: Ct));
    }

    [Fact]
    public async Task Wrong_credentials_pause_the_outbox_without_dead_letters()
    {
        using var http = new HttpClient();
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = lrs.Endpoint,
            Version = XapiVersion.V2_0_0,
            Credentials = XapiCredentials.Basic("integration_key", "wrong_secret"),
        });
        await using var outbox = new StatementOutbox(client, new InMemoryOutboxStore());
        await outbox.EnqueueAsync(Data.NewStatement(), Ct);

        var result = await outbox.FlushAsync(Ct);

        Assert.Equal(OutboxState.Paused, result.Status.State);
        Assert.Equal(1, result.Status.PendingCount);
        Assert.Equal(0, result.Status.DeadLetterCount);
    }

    /// <summary>Real sockets, with a switch to fail like a dropped network.</summary>
    private sealed class Network() : DelegatingHandler(new SocketsHttpHandler())
    {
        public volatile bool Offline;

        public volatile bool LoseNextResponse;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("Network is unreachable (simulated).");
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (LoseNextResponse)
            {
                LoseNextResponse = false;
                response.Dispose();
                throw new HttpRequestException("Connection reset after the request was sent (simulated).");
            }

            return response;
        }
    }
}
