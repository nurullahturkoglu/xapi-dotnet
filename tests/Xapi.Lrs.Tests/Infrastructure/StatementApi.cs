using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Xapi.Lrs.Tests.Infrastructure;

// Statement JSON and Statement Resource calls for the tests.
internal static class StatementApi
{
    public const string Attempted = "http://adlnet.gov/expapi/verbs/attempted";
    public const string Voided = "http://adlnet.gov/expapi/verbs/voided";

    public static string NewIri(string kind = "activities") => $"http://example.com/{kind}/{Guid.NewGuid():N}";

    public static string NewMbox() => $"mailto:{Guid.NewGuid():N}@example.com";

    public static JsonObject Statement(Guid? id = null, string? activity = null, string? mbox = null, string verb = Attempted)
    {
        var statement = new JsonObject
        {
            ["actor"] = new JsonObject { ["mbox"] = mbox ?? "mailto:learner@example.com" },
            ["verb"] = new JsonObject { ["id"] = verb },
            ["object"] = new JsonObject { ["id"] = activity ?? NewIri() },
        };
        if (id is { } value)
        {
            statement["id"] = value.ToString();
        }

        return statement;
    }

    public static JsonObject Voiding(Guid target, Guid? id = null)
    {
        var statement = Statement(id, verb: Voided);
        statement["object"] = new JsonObject { ["objectType"] = "StatementRef", ["id"] = target.ToString() };
        return statement;
    }

    public static Task<HttpResponseMessage> PutStatementAsync(this HttpClient client, Guid id, JsonNode statement) =>
        client.PutAsync($"/xapi/statements?statementId={id}", Body(statement), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostStatementsAsync(this HttpClient client, JsonNode statements) =>
        client.PostAsync("/xapi/statements", Body(statements), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> GetStatementAsync(this HttpClient client, Guid id, string parameter = "statementId") =>
        client.GetAsync($"/xapi/statements?{parameter}={id}", TestContext.Current.CancellationToken);

    public static async Task<JsonObject> GetStoredAsync(this HttpClient client, Guid id)
    {
        using var response = await client.GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadObjectAsync(response);
    }

    public static async Task<JsonObject> ReadObjectAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

    public static async Task<JsonArray> ReadArrayAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsArray();

    public static StringContent Body(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");

    public static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;
}
