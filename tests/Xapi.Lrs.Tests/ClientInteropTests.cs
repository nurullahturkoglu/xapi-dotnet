using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Attachments_and_signatures_round_trip_through_the_client(TestDatabase database, string version)
    {
        var app = await hosts.SharedAsync(database);
        using var http = app.Anonymous(version: null);
        var xapiVersion = version == "2.0.0" ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3;
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "xapi/"),
            Credentials = XapiCredentials.Basic(Credentials.All, Credentials.SecretOf(Credentials.All)),
            Version = xapiVersion,
        });
        var ct = TestContext.Current.CancellationToken;
        var verb = new Verb(new Iri(StatementApi.NewIri("verbs")));
        var actor = new Agent(new Mbox(StatementApi.NewMbox()));
        var data = MultipartApi.BinaryData(2048);
        var payload = AttachmentPayload.Create(data, "application/pdf");
        var attachment = payload.ToAttachment(new Iri("http://example.com/attachment-usage/certificate"), LanguageMap.Create("en-US", "Certificate"));
        var withData = new Statement(actor, verb, new Activity(new Iri(StatementApi.NewIri()))) { Attachments = XapiList.Create(attachment) };

        using var key = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=client interop", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = StatementSigner.Sign(new Statement(actor, verb, new Activity(new Iri(StatementApi.NewIri()))), key, xapiVersion, HashAlgorithmName.SHA384, [certificate]);

        var first = await client.SaveStatementAsync(withData, [payload], ct);
        var second = await client.SaveStatementAsync(signed.Statement, [signed.Signature], ct);

        var result = await client.QueryStatementsAsync(new StatementQuery { Verb = verb.Id, Attachments = true, Ascending = true }, ct);
        Assert.Equal([first, second], result.Statements.Select(s => s.Id!.Value));
        Assert.Equal(2, result.Attachments.Count);
        Assert.Equal(data, result.Attachments.Single(a => a.Sha2 == payload.Sha2).Content.ToArray());
        Assert.Equal("application/pdf", result.Attachments.Single(a => a.Sha2 == payload.Sha2).ContentType);
        Assert.Equal(signed.Signature.Content.ToArray(), result.Attachments.Single(a => a.Sha2 == signed.Signature.Sha2).Content.ToArray());

        // A signature that no longer matches its statement is refused
        var tampered = signed.Statement with { Id = Guid.NewGuid(), Verb = new Verb(new Iri(StatementApi.NewIri("verbs"))) };
        var error = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(tampered, [signed.Signature], ct));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, error.StatusCode);
    }
}
