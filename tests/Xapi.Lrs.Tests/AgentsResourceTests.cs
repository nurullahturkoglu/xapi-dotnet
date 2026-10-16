using System.Net;
using System.Text.Json.Nodes;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class AgentsResourceTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Person_has_the_identifier_and_the_latest_name_stored_with_the_define_scope(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var mbox = NewMbox();
        await PostAsync(app, Credentials.All, new JsonObject { ["name"] = "First", ["mbox"] = mbox });
        await PostAsync(app, Credentials.All, new JsonObject { ["name"] = "Second", ["mbox"] = mbox });
        await PostAsync(app, Credentials.Writer, new JsonObject { ["name"] = "Without define", ["mbox"] = mbox });

        var person = await GetPersonAsync(app.Client(), new JsonObject { ["mbox"] = mbox });

        Assert.Equal($$"""{"objectType":"Person","name":["Second"],"mbox":["{{mbox}}"]}""", person);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Person_lists_each_kind_of_identifier_as_an_array(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var key = Guid.NewGuid().ToString("N");
        var sha1 = (key + key)[..40];
        var openId = $"http://openid.example.com/{key}";
        var homePage = "http://accounts.example.com/";
        (JsonObject Agent, string Expected)[] cases =
        [
            (new JsonObject { ["name"] = "Hashed", ["mbox_sha1sum"] = sha1 }, $$"""{"objectType":"Person","name":["Hashed"],"mbox_sha1sum":["{{sha1}}"]}"""),
            (new JsonObject { ["openid"] = openId }, $$"""{"objectType":"Person","openid":["{{openId}}"]}"""),
            (
                new JsonObject { ["name"] = "Account", ["account"] = new JsonObject { ["homePage"] = homePage, ["name"] = key } },
                $$"""{"objectType":"Person","name":["Account"],"account":[{"homePage":"{{homePage}}","name":"{{key}}"}]}"""
            ),
        ];

        foreach (var (agent, expected) in cases)
        {
            await PostAsync(app, Credentials.All, agent.DeepClone().AsObject());
            var query = agent.DeepClone().AsObject();
            query.Remove("name");

            Assert.Equal(expected, await GetPersonAsync(app.Client(), query));
        }
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Unknown_agent_gets_a_Person_with_only_its_identifier(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var mbox = NewMbox();

        // The name in the parameter is not information the LRS has about the Agent (IEEE 9274.1.1, 4.1.6.3)
        var person = await GetPersonAsync(app.Client(), new JsonObject { ["objectType"] = "Agent", ["name"] = "Sent along", ["mbox"] = mbox });

        Assert.Equal($$"""{"objectType":"Person","mbox":["{{mbox}}"]}""", person);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_Group_with_the_same_identifier_does_not_lend_its_name(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var mbox = NewMbox();
        await PostAsync(app, Credentials.All, new JsonObject { ["objectType"] = "Group", ["name"] = "Team", ["mbox"] = mbox });

        var person = await GetPersonAsync(app.Client(), new JsonObject { ["mbox"] = mbox });

        Assert.Equal($$"""{"objectType":"Person","mbox":["{{mbox}}"]}""", person);
    }

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("2.0.0")]
    public async Task Group_members_are_known_Agents(string version)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var member = NewMbox();
        var group = new JsonObject
        {
            ["objectType"] = "Group",
            ["member"] = new JsonArray(new JsonObject { ["name"] = "Member", ["mbox"] = member }),
        };
        await PostAsync(app, Credentials.All, group);

        var person = await GetPersonAsync(app.Client(version: version), new JsonObject { ["mbox"] = member });

        Assert.Equal($$"""{"objectType":"Person","name":["Member"],"mbox":["{{member}}"]}""", person);
    }

    public static TheoryData<string> Invalid => new()
    {
        "",
        "?agent=",
        "?agent=not%20JSON",
        "?agent=%7B%22name%22%3A%22no%20identifier%22%7D",
        "?agent=%7B%22mbox%22%3A%22a%40example.com%22%7D",
        "?agent=%7B%22objectType%22%3A%22Group%22%2C%22mbox%22%3A%22mailto%3Ag%40example.com%22%7D",
        "?agent=%7B%22mbox%22%3A%22mailto%3Aa%40example.com%22%2C%22openid%22%3A%22http%3A%2F%2Fexample.com%2Fo%22%7D",
        "?agent=%7B%22mbox%22%3A%22mailto%3Aa%40example.com%22%7D&agent=%7B%22mbox%22%3A%22mailto%3Ab%40example.com%22%7D",
        "?Agent=%7B%22mbox%22%3A%22mailto%3Aa%40example.com%22%7D",
        "?agent=%7B%22mbox%22%3A%22mailto%3Aa%40example.com%22%7D&profileId=p",
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_parameters_get_400(string query)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client().GetAsync("/xapi/agents" + query, TestContext.Current.CancellationToken);

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

        using var response = await app.Client(key).GetAsync("/xapi/agents?agent=" + Uri.EscapeDataString(new JsonObject { ["mbox"] = NewMbox() }.ToJsonString()), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Writes_get_405_with_the_allowed_methods()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client().PostAsync("/xapi/agents", Body(new JsonObject()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD"], response.Content.Headers.Allow);
    }

    private static async Task PostAsync(LrsApp app, string key, JsonObject actor)
    {
        var statement = Statement();
        statement["actor"] = actor;
        using var response = await app.Client(key).PostStatementsAsync(statement);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> GetPersonAsync(HttpClient client, JsonObject agent)
    {
        using var response = await client.GetAsync("/xapi/agents?agent=" + Uri.EscapeDataString(agent.ToJsonString()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
