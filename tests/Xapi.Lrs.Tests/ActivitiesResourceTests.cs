using System.Net;
using System.Text.Json.Nodes;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class ActivitiesResourceTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Activity_is_the_canonical_definition_merged_from_every_statement(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var iri = NewIri();
        await PostAsync(app, Credentials.All, iri, new JsonObject
        {
            ["name"] = new JsonObject { ["en-US"] = "Quiz" },
            ["type"] = "http://example.com/types/a",
            ["extensions"] = new JsonObject { ["http://example.com/ext/a"] = new JsonObject { ["x"] = 1 } },
        });
        await PostAsync(app, Credentials.All, iri, new JsonObject
        {
            ["name"] = new JsonObject { ["fr-FR"] = "Examen" },
            ["description"] = new JsonObject { ["fr-FR"] = "Un examen" },
        });
        await PostAsync(app, Credentials.Writer, iri, new JsonObject { ["name"] = new JsonObject { ["de"] = "Ohne define" } });

        var activity = await GetActivityAsync(app.Client(), iri);

        Assert.Equal(
            $$$$$"""{"objectType":"Activity","id":"{{{{{iri}}}}}","definition":{"name":{"en-US":"Quiz","fr-FR":"Examen"},"description":{"fr-FR":"Un examen"},"type":"http://example.com/types/a","extensions":{"http://example.com/ext/a":{"x":1}}}}""",
            activity);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Unknown_activity_gets_an_Activity_with_only_its_id(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var iri = NewIri();

        Assert.Equal($$"""{"objectType":"Activity","id":"{{iri}}"}""", await GetActivityAsync(app.Client(), iri));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Activity_ids_are_compared_as_sent_without_normalizing(TestDatabase database)
    {
        // IEEE 9274.1.1, 4.2.7 allows simple string comparison (RFC 3987, 5.3.1): ids that would normalize to
        // the same IRI are still different Activities
        var app = await hosts.SharedAsync(database);
        var slug = Guid.NewGuid().ToString("N");
        var sent = $"http://example.com/path/../{slug}";
        await PostAsync(app, Credentials.All, sent, new JsonObject { ["name"] = new JsonObject { ["en"] = "Dot segments" } });

        Assert.Equal(
            $$$$"""{"objectType":"Activity","id":"{{{{sent}}}}","definition":{"name":{"en":"Dot segments"}}}""",
            await GetActivityAsync(app.Client(), sent));
        foreach (var other in new[] { $"http://example.com/{slug}", $"HTTP://EXAMPLE.COM/path/../{slug}", $"http://example.com/path/%2E%2E/{slug}" })
        {
            Assert.Equal($$"""{"objectType":"Activity","id":"{{other}}"}""", await GetActivityAsync(app.Client(), other));
        }
    }

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("2.0.0")]
    public async Task Both_versions_read_the_same_Activity(string version)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var iri = NewIri();
        await PostAsync(app, Credentials.All, iri, new JsonObject { ["name"] = new JsonObject { ["en"] = "Quiz" } });

        Assert.Equal(
            $$$$"""{"objectType":"Activity","id":"{{{{iri}}}}","definition":{"name":{"en":"Quiz"}}}""",
            await GetActivityAsync(app.Client(version: version), iri));
    }

    public static TheoryData<string> Invalid => new()
    {
        "",
        "?activityId=",
        "?activityId=not-an-iri",
        "?activityId=true",
        "?activityId=http%3A%2F%2Fexample.com%2Fa&activityId=http%3A%2F%2Fexample.com%2Fb",
        "?activityid=http%3A%2F%2Fexample.com%2Fa",
        "?activityId=http%3A%2F%2Fexample.com%2Fa&profileId=p",
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_parameters_get_400(string query)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client().GetAsync("/xapi/activities" + query, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(Credentials.All, HttpStatusCode.OK)]
    [InlineData(Credentials.AllRead, HttpStatusCode.OK)]
    [InlineData(Credentials.Profile, HttpStatusCode.OK)]
    [InlineData(Credentials.State, HttpStatusCode.Forbidden)]
    [InlineData(Credentials.Reader, HttpStatusCode.Forbidden)]
    [InlineData(Credentials.Writer, HttpStatusCode.Forbidden)]
    public async Task Reading_needs_the_profile_or_a_read_all_scope(string key, HttpStatusCode expected)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client(key).GetAsync("/xapi/activities?activityId=" + Uri.EscapeDataString(NewIri()), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Writes_get_405_with_the_allowed_methods()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client().PutAsync("/xapi/activities?activityId=" + Uri.EscapeDataString(NewIri()), Body(new JsonObject()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD"], response.Content.Headers.Allow);
    }

    private static async Task PostAsync(LrsApp app, string key, string iri, JsonObject definition)
    {
        var statement = Statement(activity: iri);
        statement["object"]!["definition"] = definition;
        using var response = await app.Client(key).PostStatementsAsync(statement);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> GetActivityAsync(HttpClient client, string iri)
    {
        using var response = await client.GetAsync("/xapi/activities?activityId=" + Uri.EscapeDataString(iri), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
