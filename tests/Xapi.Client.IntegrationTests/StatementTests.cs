using Xapi.Validation;

namespace Xapi.Client.IntegrationTests;

public class StatementTests(LrsqlFixture lrs)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Posted_statement_comes_back_equivalent(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var statement = Data.NewStatement();

        var id = await client.SaveStatementAsync(statement, cancellationToken: Ct);
        var stored = await client.GetStatementAsync(id, cancellationToken: Ct);

        Assert.NotNull(stored);
        Assert.Equal(id, stored.Id);
        Assert.NotNull(stored.Stored);
        Assert.NotNull(stored.Authority);
        Assert.True(StatementEquivalence.AreEquivalent(statement with { Id = id }, stored));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Put_with_id_is_idempotent_and_a_different_statement_conflicts(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var statement = Data.NewStatement() with { Id = UuidV7Generator.Shared.NewGuid() };

        Assert.Equal(statement.Id, await client.SaveStatementAsync(statement, cancellationToken: Ct));
        Assert.Equal(statement.Id, await client.SaveStatementAsync(statement, cancellationToken: Ct));
        var conflict = await Assert.ThrowsAsync<XapiConflictException>(
            () => client.SaveStatementAsync(statement with { Verb = Verbs.Failed }, cancellationToken: Ct));

        Assert.False(conflict.IsTransient);
        var stored = await client.GetStatementAsync(statement.Id!.Value, cancellationToken: Ct);
        Assert.Equal(Verbs.Completed.Id, stored?.Verb.Id);
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Batch_is_stored_with_ids_in_order(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var givenId = UuidV7Generator.Shared.NewGuid();
        Statement[] batch = [Data.NewStatement(), Data.NewStatement() with { Id = givenId }, Data.NewStatement()];

        var ids = await client.SaveStatementsAsync(batch, cancellationToken: Ct);

        Assert.Equal(3, ids.Count);
        Assert.Equal(givenId, ids[1]);
        for (var i = 0; i < batch.Length; i++)
        {
            var stored = await client.GetStatementAsync(ids[i], cancellationToken: Ct);
            Assert.NotNull(stored);
            Assert.Equal(((Agent)batch[i].Actor).Ifi, ((Agent)stored.Actor).Ifi);
        }
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Query_pages_through_more(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var registration = Guid.NewGuid();
        var posted = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            posted.Add(await client.SaveStatementAsync(Data.NewStatement(registration: registration), cancellationToken: Ct));
        }

        var query = new StatementQuery { Registration = registration, Limit = 2, Ascending = true };
        var pages = new List<StatementResult> { await client.QueryStatementsAsync(query, Ct) };
        while (pages[^1].More is { } more)
        {
            pages.Add(await client.GetMoreStatementsAsync(more, Ct));
        }

        Assert.Equal([2, 2, 1], pages.Select(p => p.Statements.Count));
        Assert.Equal(posted, pages.SelectMany(p => p.Statements).Select(s => s.Id!.Value));
        Assert.All(pages, p => Assert.NotNull(p.ConsistentThrough));

        var all = new List<Guid>();
        await foreach (var statement in client.QueryAllStatementsAsync(query with { Ascending = false }, Ct))
        {
            all.Add(statement.Id!.Value);
        }

        Assert.Equal(Enumerable.Reverse(posted), all);
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Filters_select_matching_statements(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var agent = Data.NewAgent();
        var activityId = Data.NewActivityId();
        var parentId = Data.NewActivityId();
        var registration = Guid.NewGuid();
        var withParent = Data.NewStatement(agent, activityId, registration) with
        {
            Context = new Context
            {
                Registration = registration,
                ContextActivities = new ContextActivities { Parent = [new Activity(parentId)] },
            },
        };
        var matching = await client.SaveStatementAsync(withParent, cancellationToken: Ct);
        await client.SaveStatementAsync(Data.NewStatement(Data.NewAgent(), activityId, registration) with { Verb = Verbs.Attempted }, cancellationToken: Ct);

        async Task<Guid[]> Ids(StatementQuery query) =>
            [.. (await client.QueryStatementsAsync(query with { Registration = registration }, Ct)).Statements.Select(s => s.Id!.Value)];

        Assert.Equal([matching], await Ids(new StatementQuery { Agent = agent }));
        Assert.Equal([matching], await Ids(new StatementQuery { Verb = Verbs.Completed.Id }));
        Assert.Equal(2, (await Ids(new StatementQuery { Activity = activityId })).Length);
        Assert.Empty(await Ids(new StatementQuery { Activity = parentId }));
        Assert.Equal([matching], await Ids(new StatementQuery { Activity = parentId, RelatedActivities = true }));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Voided_statement_is_only_returned_as_voided(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var id = await client.SaveStatementAsync(Data.NewStatement(), cancellationToken: Ct);

        var voidingId = await client.VoidStatementAsync(id, Agent.FromMbox("admin@example.com"), Ct);

        Assert.Null(await client.GetStatementAsync(id, cancellationToken: Ct));
        Assert.Equal(id, (await client.GetVoidedStatementAsync(id, cancellationToken: Ct))?.Id);
        var voiding = await client.GetStatementAsync(voidingId, cancellationToken: Ct);
        Assert.Equal(new StatementRef(id), voiding?.Object);
    }

    [Fact]
    public async Task Context_agents_round_trip_in_2_0()
    {
        var client = lrs.CreateClient(XapiVersion.V2_0_0);
        var statement = Data.NewStatement() with
        {
            Context = new Context
            {
                ContextAgents = [new ContextAgent(Data.NewAgent()) { RelevantTypes = [new Iri("https://example.com/types/instructor")] }],
            },
        };

        var id = await client.SaveStatementAsync(statement, cancellationToken: Ct);
        var stored = await client.GetStatementAsync(id, cancellationToken: Ct);

        Assert.NotNull(stored);
        Assert.Equal(statement.Context!.ContextAgents, stored.Context?.ContextAgents);
    }
}
