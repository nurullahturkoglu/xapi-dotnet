using System.Net;
using System.Text.Json.Nodes;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class VoidingTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_voided_statement_is_only_returned_by_voidedStatementId(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var target = Guid.NewGuid();
        var voiding = Guid.NewGuid();
        await PutAsync(client, target, Statement());

        using (var notVoided = await client.GetStatementAsync(target, "voidedStatementId"))
        {
            Assert.Equal(HttpStatusCode.NotFound, notVoided.StatusCode);
        }

        await PutAsync(client, voiding, Voiding(target));

        using (var byId = await client.GetStatementAsync(target))
        {
            Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
        }

        Assert.Equal(target.ToString(), (await GetVoidedAsync(client, target))["id"]!.GetValue<string>());
        Assert.Equal(voiding.ToString(), (await client.GetStoredAsync(voiding))["id"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_voiding_statement_cannot_be_voided(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var target = Guid.NewGuid();
        var voiding = Guid.NewGuid();
        await PutAsync(client, target, Statement());
        await PutAsync(client, voiding, Voiding(target));

        // Accepted (IEEE 9274.1.1, 4.2.5 says the LRS shall not reject it) but has no effect
        using (var post = await client.PostStatementsAsync(Voiding(voiding)))
        {
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }

        await client.GetStoredAsync(voiding);
        using var voided = await client.GetStatementAsync(voiding, "voidedStatementId");
        Assert.Equal(HttpStatusCode.NotFound, voided.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_target_that_arrives_after_its_voiding_statement_is_voided(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var target = Guid.NewGuid();

        using (var post = await client.PostStatementsAsync(Voiding(target)))
        {
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }

        await PutAsync(client, target, Statement());

        using var byId = await client.GetStatementAsync(target);
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
        await GetVoidedAsync(client, target);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_target_and_its_voiding_statement_in_one_batch(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var target = Guid.NewGuid();

        using (var post = await client.PostStatementsAsync(new JsonArray(Voiding(target), Statement(target))))
        {
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }

        await GetVoidedAsync(client, target);
    }

    private static async Task PutAsync(HttpClient client, Guid id, JsonObject statement)
    {
        using var response = await client.PutStatementAsync(id, statement);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonObject> GetVoidedAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetStatementAsync(id, "voidedStatementId");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadObjectAsync(response);
    }
}
