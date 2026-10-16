using System.Net;
using System.Text.Json.Nodes;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.DocumentApi;

namespace Xapi.Lrs.Tests;

public class DocumentConcurrencyTests(LrsHosts hosts)
{
    private const string Original = """{"v":"original"}""";
    private const string Changed = """{"v":"changed"}""";

    // The If-Match / If-None-Match matrix of IEEE 9274.1.1, 4.1.4 (RFC 9110, 13.1.1-13.1.2), the same for PUT,
    // POST and DELETE on every resource in both versions. "current" stands for the document's ETag.
    public static IEnumerable<TheoryDataRow<string, string, DocumentResource, bool, string?, string?, HttpStatusCode>> Preconditions()
    {
        var cases = new (bool Exists, string? IfMatch, string? IfNoneMatch, bool Holds)[]
        {
            (true, "current", null, true),
            (true, "\"1234\"", null, false),
            (true, "\"1111111111111111111111111111111111111111\"", null, false),
            (true, "\"1234\", current", null, true),
            (true, "unquoted", null, true),
            (true, "W/current", null, false),
            (true, "*", null, true),
            (false, "*", null, false),
            (false, "current", null, false),
            (true, null, "*", false),
            (false, null, "*", true),
            (true, null, "current", false),
            (true, null, "W/current", false),
            (true, null, "\"1234\"", true),
            (true, "current", "*", false),
        };
        foreach (var version in new[] { "2.0.0", "1.0.3" })
        {
            foreach (var method in new[] { "PUT", "POST", "DELETE" })
            {
                foreach (var resource in Enum.GetValues<DocumentResource>())
                {
                    foreach (var (exists, ifMatch, ifNoneMatch, holds) in cases)
                    {
                        yield return new(version, method, resource, exists, ifMatch, ifNoneMatch, holds ? HttpStatusCode.NoContent : HttpStatusCode.PreconditionFailed);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Preconditions))]
    public async Task Preconditions_decide_whether_a_change_happens(
        string version, string method, DocumentResource resource, bool exists, string? ifMatch, string? ifNoneMatch, HttpStatusCode expected)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client(version: version);
        var target = DocTarget.New(resource);
        var current = exists ? await client.CreateAsync(target, Original) : "\"none\"";

        string? Resolve(string? header) => header?
            .Replace("unquoted", current.Trim('"'), StringComparison.Ordinal)
            .Replace("current", current, StringComparison.Ordinal);

        using var response = method switch
        {
            "PUT" => await client.PutDocAsync(target, Changed, ifMatch: Resolve(ifMatch), ifNoneMatch: Resolve(ifNoneMatch)),
            "POST" => await client.PostDocAsync(target, Changed, ifMatch: Resolve(ifMatch), ifNoneMatch: Resolve(ifNoneMatch)),
            _ => await client.DeleteDocAsync(target, ifMatch: Resolve(ifMatch), ifNoneMatch: Resolve(ifNoneMatch)),
        };

        Assert.Equal(expected, response.StatusCode);
        using var get = await client.GetDocAsync(target);
        if (expected == HttpStatusCode.PreconditionFailed)
        {
            // Nothing changed
            Assert.Equal(exists ? HttpStatusCode.OK : HttpStatusCode.NotFound, get.StatusCode);
            if (exists)
            {
                Assert.Equal(Original, await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                Assert.Equal(current, get.Headers.ETag!.ToString());
            }
        }
        else if (method == "DELETE")
        {
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }
        else
        {
            Assert.Equal(Changed, await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(get.Headers.ETag, response.Headers.ETag);
        }
    }

    public static TheoryData<string, DocumentResource, bool, HttpStatusCode> PutWithoutPreconditions => new()
    {
        // 2.0, 4.1.4: no State exemption any more
        { "2.0.0", DocumentResource.State, true, HttpStatusCode.Conflict },
        { "2.0.0", DocumentResource.ActivityProfile, true, HttpStatusCode.Conflict },
        { "2.0.0", DocumentResource.AgentProfile, true, HttpStatusCode.Conflict },
        { "2.0.0", DocumentResource.State, false, HttpStatusCode.NoContent },
        { "2.0.0", DocumentResource.ActivityProfile, false, HttpStatusCode.NoContent },
        { "2.0.0", DocumentResource.AgentProfile, false, HttpStatusCode.NoContent },

        // 1.0.3, Communication 3.1: State permits PUT without headers; profile PUTs must carry one
        { "1.0.3", DocumentResource.State, true, HttpStatusCode.NoContent },
        { "1.0.3", DocumentResource.State, false, HttpStatusCode.NoContent },
        { "1.0.3", DocumentResource.ActivityProfile, true, HttpStatusCode.Conflict },
        { "1.0.3", DocumentResource.AgentProfile, true, HttpStatusCode.Conflict },
        { "1.0.3", DocumentResource.ActivityProfile, false, HttpStatusCode.BadRequest },
        { "1.0.3", DocumentResource.AgentProfile, false, HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(PutWithoutPreconditions))]
    public async Task Put_without_a_precondition_follows_the_version_rules(string version, DocumentResource resource, bool exists, HttpStatusCode expected)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client(version: version);
        var target = DocTarget.New(resource);
        if (exists)
        {
            await client.CreateAsync(target, Original);
        }

        using var put = await client.PutDocAsync(target, Changed);

        Assert.Equal(expected, put.StatusCode);
        using var get = await client.GetDocAsync(target);
        if (expected == HttpStatusCode.NoContent)
        {
            Assert.Equal(Changed, await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return;
        }

        Assert.Equal(exists ? HttpStatusCode.OK : HttpStatusCode.NotFound, get.StatusCode);
        if (exists)
        {
            Assert.Equal(Original, await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        if (expected == HttpStatusCode.Conflict)
        {
            // 4.1.4: the response explains how to resolve the conflict
            var problem = await StatementApi.ReadObjectAsync(put);
            Assert.Contains("If-Match", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("1.0.3")]
    public async Task Post_and_delete_need_no_precondition(string version)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client(version: version);
        foreach (var resource in Enum.GetValues<DocumentResource>())
        {
            var target = DocTarget.New(resource);
            await client.CreateAsync(target, Original);

            using (var post = await client.PostDocAsync(target, """{"w":1}"""))
            {
                Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
            }

            using (var delete = await client.DeleteDocAsync(target))
            {
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            }

            using var get = await client.GetDocAsync(target);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Concurrent_writes_with_the_same_if_match_let_exactly_one_win(DocumentResource resource, TestDatabase database)
    {
        const int Writers = 6;
        var app = await hosts.SharedAsync(database);
        var clients = Enumerable.Range(0, Writers).Select(_ => app.Client()).ToArray();
        var target = DocTarget.New(resource);
        var etag = await clients[0].CreateAsync(target, Original);

        for (var round = 0; round < 4; round++)
        {
            // Every writer read the same version; each PUT or POST carries its ETag
            var method = round % 2 == 0 ? HttpMethod.Put : HttpMethod.Post;
            using var start = new Barrier(Writers);
            var bodies = Enumerable.Range(0, Writers).Select(i => new JsonObject { ["v"] = $"round {round} writer {i}" }.ToJsonString()).ToArray();
            var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
                async () =>
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    return await clients[i].SendAsync(method, target.Url, bodies[i], ifMatch: etag);
                },
                TestContext.Current.CancellationToken)));

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.NoContent));
            Assert.Equal(Writers - 1, statuses.Count(s => s == HttpStatusCode.PreconditionFailed));

            var winner = statuses.IndexOf(HttpStatusCode.NoContent);
            var (body, current, _) = await clients[0].ReadAsync(target);
            Assert.Equal(bodies[winner], body);
            Assert.Equal(responses[winner].Headers.ETag!.ToString(), current);
            etag = current;
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Concurrent_creates_with_if_none_match_let_exactly_one_win(DocumentResource resource, TestDatabase database)
    {
        const int Writers = 6;
        var app = await hosts.SharedAsync(database);
        var target = DocTarget.New(resource);
        using var start = new Barrier(Writers);

        var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
            async () =>
            {
                var client = app.Client();
                start.SignalAndWait(TestContext.Current.CancellationToken);
                return await client.PutDocAsync(target, $$"""{"writer":{{i}}}""", ifNoneMatch: "*");
            },
            TestContext.Current.CancellationToken)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(Writers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Concurrent_merges_lose_no_property(DocumentResource resource, TestDatabase database)
    {
        const int Writers = 8;
        var app = await hosts.SharedAsync(database);
        var target = DocTarget.New(resource);
        await app.Client().CreateAsync(target, """{"start":true}""");
        using var start = new Barrier(Writers);

        var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
            async () =>
            {
                var client = app.Client();
                start.SignalAndWait(TestContext.Current.CancellationToken);
                return await client.PostDocAsync(target, $$"""{"p{{i}}":{{i}}}""");
            },
            TestContext.Current.CancellationToken)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        var merged = JsonNode.Parse((await app.Client().ReadAsync(target)).Body)!.AsObject();
        Assert.Equal(Writers + 1, merged.Count);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
