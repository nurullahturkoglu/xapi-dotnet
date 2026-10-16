using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.DocumentApi;

namespace Xapi.Lrs.Tests;

public class DocumentTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task A_document_round_trips_with_its_content_type_etag_and_last_modified(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);
        const string Body = "binary-ish ü content";
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        using (var put = await client.PutDocAsync(target, Body, "text/plain; charset=utf-8", ifNoneMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.Equal(Quoted(Sha1(Body)), put.Headers.ETag!.ToString());
        }

        using var get = await client.GetDocAsync(target);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(Body, await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/plain; charset=utf-8", get.Content.Headers.ContentType!.ToString());
        Assert.Equal(Quoted(Sha1(Body)), get.Headers.ETag!.ToString());
        Assert.False(get.Headers.ETag.IsWeak);
        var lastModified = get.Content.Headers.LastModified!.Value;
        Assert.InRange(lastModified, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal("2.0.0", StatementApi.Header(get, "X-Experience-API-Version"));
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task The_etag_is_the_sha1_of_the_content_and_follows_every_change(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);
        var first = await client.CreateAsync(target, """{"a":1}""");

        using (var post = await client.PostDocAsync(target, """{"b":2}"""))
        {
            Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
        }

        var (body, etag, _) = await client.ReadAsync(target);
        Assert.Equal("""{"a":1,"b":2}""", body);
        Assert.Equal(Quoted(Sha1(body)), etag);
        Assert.NotEqual(first, etag);

        // xAPI 1.0.3 clients see the same value: 40 lower-case hex digits in quotes (Communication 3.1)
        using var v1 = app.Client(version: "1.0.3");
        Assert.Matches("^\"[0-9a-f]{40}\"$", (await v1.ReadAsync(target)).ETag);
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Head_returns_the_headers_of_get_without_a_body(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);
        var etag = await client.CreateAsync(target, """{"a":1}""");

        using var head = await client.SendAsync(HttpMethod.Head, target.Url);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(etag, head.Headers.ETag!.ToString());
        Assert.Equal(7, head.Content.Headers.ContentLength);
        Assert.NotNull(head.Content.Headers.LastModified);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        using var headIds = await client.SendAsync(HttpMethod.Head, target.ListUrl());
        Assert.Equal(HttpStatusCode.OK, headIds.StatusCode);
        Assert.NotNull(headIds.Headers.ETag);
        Assert.Empty(await headIds.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Missing_documents_are_404_and_deleting_them_succeeds(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);

        using var get = await client.GetDocAsync(target);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var delete = await client.DeleteDocAsync(target);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Empty(await client.ListAsync(target));
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Delete_removes_one_document(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);
        var other = target.WithId("other");
        await client.CreateAsync(target);
        await client.CreateAsync(other);

        using (var delete = await client.DeleteDocAsync(target))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using var get = await client.GetDocAsync(target);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(["other"], await client.ListAsync(target));
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Post_merges_top_level_properties_and_replaces_nested_objects_whole(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);

        // POST creates a missing document as sent
        using (var create = await client.PostDocAsync(target, """{"x":"foo","y":"bar","nested":{"a":1,"b":2},"t":"ç"}"""))
        {
            Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);
        }

        using (var merge = await client.PostDocAsync(target, """{"x":"bash","z":"faz","nested":{"c":3}}""", "application/json; charset=utf-8"))
        {
            Assert.Equal(HttpStatusCode.NoContent, merge.StatusCode);
        }

        var (body, _, contentType) = await client.ReadAsync(target);
        Assert.Equal("""{"x":"bash","y":"bar","nested":{"c":3},"t":"ç","z":"faz"}""", body);
        Assert.Equal(JsonType, contentType);
    }

    public static TheoryData<string, string, string, string> Unmergeable => new()
    {
        // stored content type, stored body, posted content type, posted body
        { JsonType, """{"a":1}""", "application/octet-stream", "abcdefg" },
        { JsonType, """{"a":1}""", "not/json", "abcdefg" },
        { JsonType, """{"a":1}""", "application/x-www-form-urlencoded", "abc" },
        { JsonType, """{"a":1}""", JsonType, """{"b":2}{""" },
        { JsonType, """{"a":1}""", JsonType, """[1,2]""" },
        { JsonType, """{"a":1}""", JsonType, "\"text\"" },
        { "application/octet-stream", "/ asdf / undefined", JsonType, """{"b":2}""" },
        { JsonType, """[1]""", JsonType, """{"b":2}""" },
        { JsonType, """{"a":""", JsonType, """{"b":2}""" },
    };

    [Theory]
    [MemberData(nameof(Unmergeable))]
    public async Task Post_is_rejected_unless_both_documents_are_json_objects(string storedType, string stored, string postedType, string posted)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client();
        foreach (var resource in Enum.GetValues<DocumentResource>())
        {
            var target = DocTarget.New(resource);
            var etag = await client.CreateAsync(target, stored, storedType);

            using var post = await client.PostDocAsync(target, posted, postedType);

            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
            var (body, after, contentType) = await client.ReadAsync(target);
            Assert.Equal(stored, body);
            Assert.Equal(etag, after);
            Assert.Equal(storedType, contentType);
        }
    }

    [Theory]
    [InlineData("""{"b":2}{""")]
    [InlineData("[1]")]
    [InlineData("")]
    public async Task Post_of_json_that_is_not_an_object_is_rejected_for_a_new_document(string posted)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client();
        var target = DocTarget.New(DocumentResource.ActivityProfile);

        using var post = await client.PostDocAsync(target, posted);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        using var get = await client.GetDocAsync(target);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Post_of_another_content_type_creates_a_new_document_as_sent()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client();
        var target = DocTarget.New(DocumentResource.State);

        using var post = await client.PostDocAsync(target, "plain", "text/plain");

        Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
        var (body, _, contentType) = await client.ReadAsync(target);
        Assert.Equal("plain", body);
        Assert.Equal("text/plain", contentType);
    }

    [Fact]
    public async Task A_document_without_a_content_type_is_stored_as_octet_stream()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client();
        var target = DocTarget.New(DocumentResource.AgentProfile);

        using var put = await client.PutDocAsync(target, "raw", contentType: null, ifNoneMatch: "*");

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal("application/octet-stream", (await client.ReadAsync(target)).ContentType);
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Listing_returns_sorted_ids_and_the_newest_last_modified(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);

        using (var empty = await client.SendAsync(HttpMethod.Get, target.ListUrl()))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Equal("[]", await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(JsonType, empty.Content.Headers.ContentType!.MediaType);
            Assert.Null(empty.Content.Headers.LastModified);
        }

        foreach (var id in new[] { "b", "a", "c ü/?&" })
        {
            await client.CreateAsync(target.WithId(id));
        }

        using var list = await client.SendAsync(HttpMethod.Get, target.ListUrl());
        Assert.Equal(["a", "b", "c ü/?&"], (await StatementApi.ReadArrayAsync(list)).Select(n => n!.GetValue<string>()));
        Assert.NotNull(list.Headers.ETag);

        using var context = app.CreateDbContext();
        var newest = (await UpdatedAsync(context, target)).Max(d => d.Updated);
        Assert.Equal(TruncateToSeconds(newest), list.Content.Headers.LastModified);
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task Since_lists_only_documents_changed_after_it(DocumentResource resource, TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var target = DocTarget.New(resource);
        await client.CreateAsync(target.WithId("old"));
        await client.CreateAsync(target.WithId("new"));

        DateTimeOffset oldUpdated;
        using (var context = app.CreateDbContext())
        {
            oldUpdated = (await UpdatedAsync(context, target)).Single(d => d.Id == "old").Updated;
        }

        // Exclusive: a document changed exactly at since is not listed
        Assert.Equal(["new"], await client.ListAsync(target, XapiTimestamp.Format(oldUpdated)));
        Assert.Equal(["new", "old"], await client.ListAsync(target, XapiTimestamp.Format(oldUpdated.AddTicks(-10))));
        Assert.Empty(await client.ListAsync(target, XapiTimestamp.Format(DateTimeOffset.UtcNow.AddMinutes(1))));

        // Offsets are converted, and changing a document moves it past since
        var cut = DateTimeOffset.UtcNow;
        using (var post = await client.PostDocAsync(target.WithId("old"), """{"b":1}"""))
        {
            Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
        }

        Assert.Equal(["old"], await client.ListAsync(target, cut.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task State_documents_are_kept_apart_by_registration(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var plain = DocTarget.New(DocumentResource.State).WithId("s");
        var r1 = plain with { Registration = Guid.NewGuid() };
        var r2 = plain with { Registration = Guid.NewGuid() };
        await client.CreateAsync(plain, """{"r":0}""");
        await client.CreateAsync(r1, """{"r":1}""");
        await client.CreateAsync(r2, """{"r":2}""");
        await client.CreateAsync(r2.WithId("only-r2"), """{"r":2}""");

        Assert.Equal("""{"r":0}""", (await client.ReadAsync(plain)).Body);
        Assert.Equal("""{"r":1}""", (await client.ReadAsync(r1)).Body);
        Assert.Equal(["only-r2", "s"], await client.ListAsync(r2));
        Assert.Equal(["s"], await client.ListAsync(r1));

        // Without a registration, a listing covers the Activity and Agent across registrations
        // (4.1.6.2: "Activity + Agent [+ registration if specified]")
        Assert.Equal(["only-r2", "s"], await client.ListAsync(plain));

        // Uppercase UUIDs name the same registration
        using var upper = await client.SendAsync(HttpMethod.Get, r1.Url.Replace(r1.Registration!.Value.ToString(), r1.Registration.Value.ToString().ToUpperInvariant(), StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, upper.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Deleting_all_state_honours_the_registration(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var plain = DocTarget.New(DocumentResource.State);
        var r1 = plain with { Registration = Guid.NewGuid() };
        var r2 = plain with { Registration = Guid.NewGuid() };
        var otherAgent = DocTarget.New(DocumentResource.State) with { ActivityId = plain.ActivityId };
        var otherActivity = plain with { ActivityId = StatementApi.NewIri() };
        foreach (var target in new[] { plain, r1, r1.WithId("second"), r2, otherAgent, otherActivity })
        {
            await client.CreateAsync(target);
        }

        using (var delete = await client.SendAsync(HttpMethod.Delete, r1.ListUrl()))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.Empty(await client.ListAsync(r1));
        Assert.Equal([r2.Id], await client.ListAsync(r2));
        using (var kept = await client.GetDocAsync(plain))
        {
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        }

        using (var delete = await client.SendAsync(HttpMethod.Delete, plain.ListUrl()))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.Empty(await client.ListAsync(plain));
        Assert.Empty(await client.ListAsync(r2));
        Assert.Equal([otherAgent.Id], await client.ListAsync(otherAgent));
        Assert.Equal([otherActivity.Id], await client.ListAsync(otherActivity));
    }

    [Theory]
    [MemberData(nameof(ResourcesAndDatabases), MemberType = typeof(DocumentApi))]
    public async Task The_agent_is_matched_by_its_identifier_only(DocumentResource resource, TestDatabase database)
    {
        if (resource == DocumentResource.ActivityProfile)
        {
            return;
        }

        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var account = new JsonObject { ["homePage"] = "http://www.example.com/agentId/1", ["name"] = Guid.NewGuid().ToString() };
        var target = DocTarget.New(resource) with { Agent = new JsonObject { ["objectType"] = "Agent", ["account"] = account.DeepClone() }.ToJsonString() };
        await client.CreateAsync(target, """{"a":1}""");

        var renamed = target with { Agent = new JsonObject { ["account"] = account.DeepClone(), ["name"] = "Someone" }.ToJsonString() };
        Assert.Equal("""{"a":1}""", (await client.ReadAsync(renamed)).Body);
    }

    [Fact]
    public async Task Documents_larger_than_the_limit_get_413()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.Configure<XapiLrsOptions>(o => o.MaxDocumentBytes = 16));
        var client = app.Client();
        var target = DocTarget.New(DocumentResource.State);

        using (var put = await client.PutDocAsync(target, new string('x', 17), "text/plain"))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, put.StatusCode);
        }

        // Without Content-Length the limit is checked while reading
        var request = new HttpRequestMessage(HttpMethod.Put, target.Url)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100)))),
        };
        request.Headers.TransferEncodingChunked = true;
        using (var chunked = await client.SendAsync(request, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        }

        // The merged document counts too
        await client.CreateAsync(target, """{"a":"1234567"}""");
        using (var merge = await client.PostDocAsync(target, """{"b":"1234"}"""))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, merge.StatusCode);
        }

        Assert.Equal("""{"a":"1234567"}""", (await client.ReadAsync(target)).Body);
    }

    [Theory]
    [InlineData("/xapi/activities/state", "GET, HEAD, PUT, POST, DELETE")]
    [InlineData("/xapi/activities/profile", "GET, HEAD, PUT, POST, DELETE")]
    [InlineData("/xapi/agents/profile", "GET, HEAD, PUT, POST, DELETE")]
    public async Task Other_methods_get_405_with_the_allowed_methods(string path, string allowed)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await app.Client().SendAsync(HttpMethod.Patch, path);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(allowed, string.Join(", ", response.Content.Headers.Allow));
    }

#pragma warning disable CA5350 // the ETag algorithm of xAPI 1.0.3, not security
#pragma warning disable CA5350 // the ETag algorithm of xAPI 1.0.3, not used for security
    private static string Sha1(string text) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
#pragma warning restore CA5350

    private static string Quoted(string etag) => $"\"{etag}\"";

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    // The stored change times of the documents in the target's scope
    private static async Task<List<(string Id, DateTimeOffset Updated)>> UpdatedAsync(XapiLrsDbContext context, DocTarget target)
    {
        var ct = TestContext.Current.CancellationToken;
        var ifi = "mbox:" + JsonNode.Parse(target.Agent)!["mbox"]!.GetValue<string>();
        var rows = target.Resource switch
        {
            DocumentResource.State => await context.StateDocuments.Where(d => d.ActivityIri == target.ActivityId && d.AgentIfi == ifi)
                .Select(d => new { d.DocumentId, d.Updated }).ToListAsync(ct),
            DocumentResource.ActivityProfile => await context.ActivityProfiles.Where(d => d.ActivityIri == target.ActivityId)
                .Select(d => new { d.DocumentId, d.Updated }).ToListAsync(ct),
            _ => await context.AgentProfiles.Where(d => d.AgentIfi == ifi).Select(d => new { d.DocumentId, d.Updated }).ToListAsync(ct),
        };
        return [.. rows.Select(r => (r.DocumentId, r.Updated))];
    }
}
