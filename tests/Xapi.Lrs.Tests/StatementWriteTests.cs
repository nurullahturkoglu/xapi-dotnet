using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class StatementWriteTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Put_stores_the_statement_with_the_properties_the_lrs_sets(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var id = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var statement = Statement();
        statement["stored"] = "2001-01-01T00:00:00.000Z";

        using (var put = await client.PutStatementAsync(id, statement))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.Equal("2.0.0", Header(put, "X-Experience-API-Version"));
        }

        using var response = await client.GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadObjectAsync(response);
        Assert.Equal(id.ToString(), stored["id"]!.GetValue<string>());
        Assert.Equal("2.0.0", stored["version"]!.GetValue<string>());
        var storedTime = DateTimeOffset.Parse(stored["stored"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        Assert.InRange(storedTime, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(stored["stored"]!.GetValue<string>(), stored["timestamp"]!.GetValue<string>());
        Assert.NotNull(stored["authority"]);
        Assert.Equal(storedTime.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture), Header(response, "Last-Modified"));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task The_version_property_defaults_to_the_requests_version(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var withVersion = Statement();
        withVersion["version"] = "1.0.3";

        using (var put = await app.Client(version: "1.0.3").PutStatementAsync(v1, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using (var put = await app.Client(version: "2.0.0").PutStatementAsync(v2, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using (var put = await app.Client(version: "2.0.0").PutStatementAsync(kept, withVersion))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var client = app.Client();
        Assert.Equal("1.0.0", (await client.GetStoredAsync(v1))["version"]!.GetValue<string>());
        Assert.Equal("2.0.0", (await client.GetStoredAsync(v2))["version"]!.GetValue<string>());
        Assert.Equal("1.0.3", (await client.GetStoredAsync(kept))["version"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Putting_the_same_statement_again_changes_nothing(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var id = Guid.NewGuid();
        var statement = Statement();

        using (var first = await client.PutStatementAsync(id, statement))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        var stored = (await client.GetStoredAsync(id))["stored"]!.GetValue<string>();

        // Equivalent, not identical: property order and the objectType default differ
        var reordered = new JsonObject
        {
            ["object"] = new JsonObject { ["objectType"] = "Activity", ["id"] = statement["object"]!["id"]!.GetValue<string>() },
            ["verb"] = statement["verb"]!.DeepClone(),
            ["actor"] = statement["actor"]!.DeepClone(),
        };
        using (var again = await client.PutStatementAsync(id, reordered))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        using (var post = await client.PostStatementsAsync(new JsonArray(Statement(id, activity: statement["object"]!["id"]!.GetValue<string>()))))
        {
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }

        Assert.Equal(stored, (await client.GetStoredAsync(id))["stored"]!.GetValue<string>());
        using var context = app.CreateDbContext();
        Assert.Equal(1, await context.Statements.CountAsync(s => s.StatementId == id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_different_statement_with_a_stored_id_is_a_conflict(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var id = Guid.NewGuid();
        var original = Statement(mbox: "mailto:first@example.com");

        using (var first = await client.PutStatementAsync(id, original))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using (var put = await client.PutStatementAsync(id, Statement(mbox: "mailto:second@example.com")))
        {
            Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        }

        using (var post = await client.PostStatementsAsync(Statement(id, mbox: "mailto:third@example.com")))
        {
            Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        }

        Assert.Equal("mailto:first@example.com", (await client.GetStoredAsync(id))["actor"]!["mbox"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Post_returns_the_ids_in_order_and_assigns_missing_ones(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var given = Guid.NewGuid();

        using var response = await client.PostStatementsAsync(new JsonArray(Statement(), Statement(given), Statement()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = (await ReadArrayAsync(response)).Select(i => Guid.Parse(i!.GetValue<string>())).ToList();
        Assert.Equal(3, ids.Count);
        Assert.Equal(given, ids[1]);
        Assert.Equal(3, ids.Distinct().Count());
        foreach (var id in ids)
        {
            Assert.Equal(id.ToString(), (await client.GetStoredAsync(id))["id"]!.GetValue<string>());
        }
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_batch_that_repeats_an_id_is_rejected_and_stores_nothing(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var other = Guid.NewGuid();
        var repeated = Guid.NewGuid();

        using var response = await client.PostStatementsAsync(new JsonArray(Statement(other), Statement(repeated), Statement(repeated)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("more than once", (await ReadObjectAsync(response))["detail"]!.GetValue<string>(), StringComparison.Ordinal);
        using var get = await client.GetStatementAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_batch_with_a_conflict_stores_none_of_its_statements(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var existing = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        using (var put = await client.PutStatementAsync(existing, Statement(mbox: "mailto:first@example.com")))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var response = await client.PostStatementsAsync(new JsonArray(Statement(fresh), Statement(existing, mbox: "mailto:second@example.com")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var get = await client.GetStatementAsync(fresh);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected_with_400()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client();
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        async Task AssertBadRequest(HttpResponseMessage response)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            }

            using var get = await client.GetStatementAsync(id);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }

        await AssertBadRequest(await client.PutAsync("/xapi/statements", Body(Statement()), ct));
        await AssertBadRequest(await client.PutAsync("/xapi/statements?statementId=not-a-uuid", Body(Statement()), ct));
        await AssertBadRequest(await client.PutStatementAsync(id, Statement(Guid.NewGuid())));
        await AssertBadRequest(await client.PutStatementAsync(id, new JsonObject { ["actor"] = new JsonObject() }));
        var unknownProperty = Statement();
        unknownProperty["unknown"] = 1;
        await AssertBadRequest(await client.PutStatementAsync(id, unknownProperty));
        await AssertBadRequest(await client.PutAsync($"/xapi/statements?statementId={id}", new StringContent(Statement().ToJsonString(), Encoding.UTF8, "text/plain"), ct));
        await AssertBadRequest(await client.PostAsync("/xapi/statements?method=PUT", Body(Statement(id)), ct));
        await AssertBadRequest(await client.PostStatementsAsync(new JsonArray(Statement(id), new JsonObject())));
    }

    [Fact]
    public async Task Multipart_statement_requests_are_rejected_with_400()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var id = Guid.NewGuid();
        using var content = new MultipartContent("mixed");
        content.Add(Body(Statement(id)));

        using var response = await app.Client().PostAsync("/xapi/statements", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("multipart/mixed", (await ReadObjectAsync(response))["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Concurrent_puts_of_the_same_statement_store_it_once(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var id = Guid.NewGuid();
        var statement = Statement();
        var clients = Enumerable.Range(0, 8).Select(_ => app.Client()).ToList();

        var responses = await Task.WhenAll(clients.Select(c => c.PutStatementAsync(id, statement)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        using var context = app.CreateDbContext();
        Assert.Equal(1, await context.Statements.CountAsync(s => s.StatementId == id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Concurrent_puts_of_different_statements_with_one_id_store_exactly_one(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var id = Guid.NewGuid();
        var statements = Enumerable.Range(0, 8).Select(i => Statement(mbox: $"mailto:writer{i}@example.com")).ToList();

        var responses = await Task.WhenAll(statements.Select(s => app.Client().PutStatementAsync(id, s)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var winner = statements[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.NoContent)];
        Assert.Equal(winner["actor"]!["mbox"]!.GetValue<string>(), (await app.Client().GetStoredAsync(id))["actor"]!["mbox"]!.GetValue<string>());
    }
}
