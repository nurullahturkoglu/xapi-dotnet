using System.Net;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;

namespace Xapi.Lrs.Tests;

public class DocumentParameterTests(LrsHosts hosts)
{
    private const string Activity = "activityId=http%3A%2F%2Fexample.com%2Fa";
    private const string Agent = "agent=%7B%22mbox%22%3A%22mailto%3Aa%40example.com%22%7D";
    private const string State = "/xapi/activities/state";
    private const string ActivityProfile = "/xapi/activities/profile";
    private const string AgentProfile = "/xapi/agents/profile";

    // Each request is rejected with 400 before anything is stored (IEEE 9274.1.1, 4.1.5 and 4.1.6)
    public static TheoryData<string, string> Invalid => new()
    {
        // Required parameters
        { "PUT", $"{State}?{Agent}&stateId=s" },
        { "GET", $"{State}?{Agent}" },
        { "DELETE", $"{State}?{Activity}" },
        { "POST", $"{State}?{Activity}&{Agent}" },
        { "PUT", $"{State}?{Activity}&{Agent}" },
        { "PUT", $"{ActivityProfile}?{Activity}" },
        { "POST", $"{ActivityProfile}?profileId=p" },
        { "DELETE", $"{ActivityProfile}?{Activity}" },
        { "GET", $"{ActivityProfile}" },
        { "PUT", $"{AgentProfile}?{Agent}" },
        { "DELETE", $"{AgentProfile}?{Agent}" },
        { "GET", $"{AgentProfile}?profileId=p" },

        // Formats
        { "GET", $"{State}?activityId=not-an-iri&{Agent}&stateId=s" },
        { "GET", $"{State}?activityId=&{Agent}&stateId=s" },
        { "GET", $"{State}?{Activity}&agent=true&stateId=s" },
        { "GET", $"{State}?{Activity}&agent=not%20JSON&stateId=s" },
        { "GET", $"{State}?{Activity}&agent=%7B%22objectType%22%22Agent%22%7D&stateId=s" },
        { "GET", $"{State}?{Activity}&agent=%7B%22name%22%3A%22no%20ifi%22%7D&stateId=s" },
        { "GET", $"{State}?{Activity}&agent=%7B%22mbox%22%3A%22a%40example.com%22%7D&stateId=s" },
        { "GET", $"{AgentProfile}?agent=%7B%22objectType%22%3A%22Group%22%2C%22mbox%22%3A%22mailto%3Ag%40example.com%22%7D&profileId=p" },
        { "GET", $"{State}?{Activity}&{Agent}&registration=true&stateId=s" },
        { "DELETE", $"{State}?{Activity}&{Agent}&registration=a2a4bcb6f5b04aa0b3b9e1a7e1b2d3c4" },
        { "GET", $"{State}?{Activity}&{Agent}&since=not%20a%20timestamp" },
        { "GET", $"{AgentProfile}?{Agent}&since=true" },
        { "GET", $"{ActivityProfile}?{Activity}&since=2026-10-17T10%3A00%3A00" },

        // Combinations, unknown and miscased parameters
        { "GET", $"{State}?{Activity}&{Agent}&stateId=s&since=2026-10-17T10%3A00%3A00Z" },
        { "GET", $"{ActivityProfile}?{Activity}&profileId=p&since=2026-10-17T10%3A00%3A00Z" },
        { "PUT", $"{State}?{Activity}&{Agent}&stateId=s&since=2026-10-17T10%3A00%3A00Z" },
        { "DELETE", $"{State}?{Activity}&{Agent}&since=2026-10-17T10%3A00%3A00Z" },
        { "GET", $"{ActivityProfile}?{Activity}&profileId=p&registration=a2a4bcb6-f5b0-4aa0-b3b9-e1a7e1b2d3c4" },
        { "GET", $"{AgentProfile}?{Agent}&{Activity}&profileId=p" },
        { "GET", $"{ActivityProfile}?{Activity}&{Agent}&profileId=p" },
        { "GET", $"{State}?{Activity}&{Agent}&stateid=s" },
        { "GET", $"{State}?ActivityId=http%3A%2F%2Fexample.com%2Fa&{Agent}&stateId=s" },
        { "GET", $"{State}?{Activity}&{Agent}&stateId=s&stateId=t" },
        { "GET", $"{State}?{Activity}&{Agent}&stateId=s&method=PUT" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_parameters_get_400(string method, string url)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client().SendAsync(new HttpMethod(method), url, method is "PUT" or "POST" ? """{"a":1}""" : null, ifNoneMatch: method == "PUT" ? "*" : null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(State)]
    [InlineData(ActivityProfile)]
    [InlineData(AgentProfile)]
    public async Task Requests_without_a_version_header_get_400_with_the_version_header(string path)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Anonymous(version: null);
        client.DefaultRequestHeaders.Authorization = Basic(Credentials.All, Credentials.SecretOf(Credentials.All));

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("2.0.0", StatementApi.Header(response, "X-Experience-API-Version"));
    }

    public static TheoryData<string, string, bool, bool> ScopeCases => new()
    {
        // credential, path, may read, may write
        { Credentials.State, State, true, true },
        { Credentials.State, ActivityProfile, false, false },
        { Credentials.State, AgentProfile, false, false },
        { Credentials.Profile, State, false, false },
        { Credentials.Profile, ActivityProfile, true, true },
        { Credentials.Profile, AgentProfile, true, true },
        { Credentials.AllRead, State, true, false },
        { Credentials.AllRead, AgentProfile, true, false },
        { Credentials.Writer, State, false, false },
        { Credentials.Reader, ActivityProfile, false, false },
        { Credentials.All, AgentProfile, true, true },
    };

    [Theory]
    [MemberData(nameof(ScopeCases))]
    public async Task Scopes_limit_access_to_documents(string credential, string path, bool mayRead, bool mayWrite)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client(credential);
        var id = Guid.NewGuid();
        var query = path switch
        {
            State => $"?{Activity}&{Agent}&stateId={id}",
            ActivityProfile => $"?{Activity}&profileId={id}",
            _ => $"?{Agent}&profileId={id}",
        };

        using var put = await client.SendAsync(HttpMethod.Put, path + query, """{"a":1}""", ifMatch: "*");
        using var post = await client.SendAsync(HttpMethod.Post, path + query, """{"a":1}""");
        using var delete = await client.SendAsync(HttpMethod.Delete, path + query, ifMatch: "\"x\"");
        using var get = await client.SendAsync(HttpMethod.Get, path + query);

        // A permitted write gets past the scope check to the precondition (412), the merge (204) or the delete
        Assert.Equal(mayWrite ? HttpStatusCode.PreconditionFailed : HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(mayWrite ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(mayWrite ? HttpStatusCode.PreconditionFailed : HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden == get.StatusCode, mayRead);
    }
}
