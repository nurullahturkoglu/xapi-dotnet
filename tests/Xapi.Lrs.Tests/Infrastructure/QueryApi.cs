using System.Net;
using System.Text.Json.Nodes;

namespace Xapi.Lrs.Tests.Infrastructure;

// Statement queries (GET /statements without statementId) for the tests.
internal static class QueryApi
{
    public static string Query(params (string Name, string Value)[] parameters) =>
        string.Join("&", parameters.Select(p => p.Name + "=" + Uri.EscapeDataString(p.Value)));

    public static Task<HttpResponseMessage> GetStatementsAsync(this HttpClient client, string query) =>
        client.GetAsync("/xapi/statements" + (query.Length == 0 ? "" : "?" + query), TestContext.Current.CancellationToken);

    public static async Task<JsonObject> QueryAsync(this HttpClient client, string query)
    {
        using var response = await client.GetStatementsAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await StatementApi.ReadObjectAsync(response);
    }

    // The next page: "more" is a relative IRL with the full path
    public static async Task<JsonObject> MoreAsync(this HttpClient client, JsonObject result)
    {
        var more = More(result);
        Assert.NotEmpty(more);
        Assert.StartsWith("/xapi/statements?", more, StringComparison.Ordinal);
        using var response = await client.GetAsync(more, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await StatementApi.ReadObjectAsync(response);
    }

    public static string More(JsonObject result) => result["more"]!.GetValue<string>();

    public static List<Guid> Ids(JsonObject result) =>
        [.. result["statements"]!.AsArray().Select(s => Guid.Parse(s!["id"]!.GetValue<string>()))];

    public static JsonObject Single(JsonObject result, Guid id) =>
        result["statements"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id.ToString())!.AsObject();

    // Stores the statements one by one, so their stored order is the list order
    public static async Task<List<Guid>> PutAllAsync(this HttpClient client, params JsonObject[] statements)
    {
        var ids = new List<Guid>();
        foreach (var statement in statements)
        {
            var id = Guid.NewGuid();
            using var response = await client.PutStatementAsync(id, statement);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            ids.Add(id);
        }

        return ids;
    }
}
