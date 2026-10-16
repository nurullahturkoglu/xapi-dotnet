using System.Text;
using System.Text.Json;
using Xapi.Client;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;

namespace Xapi.Lrs.Tests;

// Xapi.Client's document API, tested against lrsql in its own integration tests, run against this LRS.
public class DocumentClientTests(LrsHosts hosts)
{
    private static readonly string[] Bookmark = ["bookmark"];
    private static readonly string[] Settings = ["settings"];

    public static TheoryData<string> Versions => new() { "2.0.0", "1.0.3" };

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task State_documents_work_through_the_client(string version)
    {
        var client = await ClientAsync(version);
        var key = new StateKey(new Iri(StatementApi.NewIri()), new Agent(new Mbox(StatementApi.NewMbox())), "bookmark", Guid.NewGuid());
        var ct = TestContext.Current.CancellationToken;

        await client.PutDocumentAsync(key, Json("""{"page":1,"seen":[1]}"""), DocumentPrecondition.IfNoneMatchAny, ct);
        var stored = await client.GetDocumentAsync(key, ct);
        Assert.NotNull(stored);
        Assert.NotNull(stored.ETag);
        Assert.NotNull(stored.LastModified);

        await client.MergeDocumentAsync(key, JsonDocument.Parse("""{"page":2}""").RootElement, DocumentPrecondition.IfMatch(stored.ETag), ct);
        var merged = await client.GetDocumentAsync(key, ct);
        Assert.Equal("""{"page":2,"seen":[1]}""", Encoding.UTF8.GetString(merged!.Content.Span));

        // The old ETag no longer matches
        await Assert.ThrowsAsync<XapiPreconditionFailedException>(() =>
            client.PutDocumentAsync(key, Json("{}"), DocumentPrecondition.IfMatch(stored.ETag), ct));

        Assert.Equal(Bookmark, await client.GetStateIdsAsync(key.ActivityId, key.Agent, key.Registration, cancellationToken: ct));
        Assert.Empty(await client.GetStateIdsAsync(key.ActivityId, key.Agent, key.Registration, since: DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken: ct));

        await client.DeleteStatesAsync(key.ActivityId, key.Agent, key.Registration, ct);
        Assert.Null(await client.GetDocumentAsync(key, ct));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Profile_documents_work_through_the_client(string version)
    {
        var client = await ClientAsync(version);
        var ct = TestContext.Current.CancellationToken;
        var agent = new Agent(new Mbox(StatementApi.NewMbox()));
        var activity = new Iri(StatementApi.NewIri());
        DocumentKey[] keys = [new AgentProfileKey(agent, "settings"), new ActivityProfileKey(activity, "settings")];

        foreach (var key in keys)
        {
            await client.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes("v1"), "text/plain"), DocumentPrecondition.IfNoneMatchAny, ct);

            // A blind overwrite of an existing document is refused in both versions
            await Assert.ThrowsAsync<XapiConflictException>(() =>
                client.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes("v2"), "text/plain"), cancellationToken: ct));

            var stored = await client.GetDocumentAsync(key, ct);
            Assert.Equal("v1", Encoding.UTF8.GetString(stored!.Content.Span));
            Assert.Equal("text/plain", stored.ContentType);

            await client.DeleteDocumentAsync(key, DocumentPrecondition.IfMatch(stored.ETag!), ct);
            Assert.Null(await client.GetDocumentAsync(key, ct));
        }

        await client.PutDocumentAsync(keys[0], Json("{}"), DocumentPrecondition.IfNoneMatchAny, ct);
        Assert.Equal(Settings, await client.GetAgentProfileIdsAsync(agent, cancellationToken: ct));
        Assert.Empty(await client.GetActivityProfileIdsAsync(activity, cancellationToken: ct));
    }

    private async Task<XapiClient> ClientAsync(string version)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        return new XapiClient(app.Anonymous(version: null), new XapiClientOptions
        {
            Endpoint = new Uri("http://localhost/xapi/"),
            Version = version == "2.0.0" ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3,
            Credentials = XapiCredentials.Basic(Credentials.All, Credentials.SecretOf(Credentials.All)),
        });
    }

    private static XapiDocument Json(string json) => new(Encoding.UTF8.GetBytes(json), "application/json");
}
