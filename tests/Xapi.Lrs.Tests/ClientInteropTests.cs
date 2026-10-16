using Xapi.Client;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;

namespace Xapi.Lrs.Tests;

// Xapi.Client against this LRS in memory: both halves of the library agree on the Statement Resource.
public class ClientInteropTests(LrsHosts hosts)
{
    public static IEnumerable<TheoryDataRow<TestDatabase, string>> Cases() =>
        from database in TestDatabases.All()
        from version in new[] { "1.0.3", "2.0.0" }
        select new TheoryDataRow<TestDatabase, string>(database.Data, version) { Skip = database.Skip };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_client_pages_through_a_query_and_reads_voided_statements(TestDatabase database, string version)
    {
        var app = await hosts.SharedAsync(database);
        using var http = app.Anonymous(version: null);
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "xapi/"),
            Credentials = XapiCredentials.Basic(Credentials.All, Credentials.SecretOf(Credentials.All)),
            Version = version == "2.0.0" ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3,
        });
        var ct = TestContext.Current.CancellationToken;
        var verb = new Verb(new Iri(StatementApi.NewIri("verbs")));
        var actor = new Agent(new Mbox(StatementApi.NewMbox()));
        var statements = Enumerable.Range(0, 5)
            .Select(i => new Statement(actor, verb, new Activity(new Iri($"http://example.com/activities/{i}"))))
            .ToList();

        var ids = new List<Guid>();
        foreach (var statement in statements)
        {
            ids.Add(await client.SaveStatementAsync(statement, cancellationToken: ct));
        }

        var voided = ids[2];
        var voiding = await client.VoidStatementAsync(voided, actor, ct);

        var query = new StatementQuery { Agent = actor, Verb = verb.Id, Limit = 2, Ascending = true };
        var first = await client.QueryStatementsAsync(query, ct);
        Assert.Equal(2, first.Statements.Count);
        Assert.NotNull(first.More);
        Assert.NotNull(first.ConsistentThrough);

        var all = new List<Guid>();
        await foreach (var statement in client.QueryAllStatementsAsync(query, ct))
        {
            all.Add(statement.Id!.Value);
        }

        // The voided statement is left out; the voiding statement matches the filters through its StatementRef
        Assert.Equal([.. ids.Where(id => id != voided), voiding], all);
        Assert.Null(await client.GetStatementAsync(voided, cancellationToken: ct));
        Assert.Equal(voided, (await client.GetVoidedStatementAsync(voided, cancellationToken: ct))!.Id);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_client_reads_Persons_and_Activities(TestDatabase database, string version)
    {
        var app = await hosts.SharedAsync(database);
        using var http = app.Anonymous(version: null);
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "xapi/"),
            Credentials = XapiCredentials.Basic(Credentials.All, Credentials.SecretOf(Credentials.All)),
            Version = version == "2.0.0" ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3,
        });
        var ct = TestContext.Current.CancellationToken;
        var account = new Account(new Iri("http://accounts.example.com/"), Guid.NewGuid().ToString("N"));
        var activityId = new Iri(StatementApi.NewIri());
        var verb = new Verb(new Iri(StatementApi.NewIri("verbs")));
        LanguageMap Name(string language, string text) => LanguageMap.Empty.With(language, text);

        await client.SaveStatementAsync(
            new Statement(new Agent(account) { Name = "Learner" }, verb, new Activity(activityId) { Definition = new ActivityDefinition { Name = Name("en-US", "Quiz") } }),
            cancellationToken: ct);
        await client.SaveStatementAsync(
            new Statement(new Agent(account), verb, new Activity(activityId) { Definition = new ActivityDefinition { Name = Name("fr-FR", "Examen") } }),
            cancellationToken: ct);

        var person = await client.GetPersonAsync(new Agent(account), ct);
        Assert.Equal(new Person { Names = ["Learner"], Accounts = [account] }, person);

        var activity = await client.GetActivityAsync(activityId, ct);
        Assert.Equal(new Activity(activityId) { Definition = new ActivityDefinition { Name = Name("en-US", "Quiz").With("fr-FR", "Examen") } }, activity);

        var unknown = new Iri(StatementApi.NewIri());
        Assert.Equal(new Activity(unknown), await client.GetActivityAsync(unknown, ct));
        var stranger = new Agent(new Mbox(StatementApi.NewMbox()));
        Assert.Equal(new Person { Mboxes = [(Mbox)stranger.Ifi] }, await client.GetPersonAsync(stranger, ct));
    }
}
