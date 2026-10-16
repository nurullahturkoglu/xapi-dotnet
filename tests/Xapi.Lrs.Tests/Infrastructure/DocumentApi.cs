using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Tests.Infrastructure;

// A document address on one of the three document resources, with a fresh Activity and Agent so tests on a
// shared LRS do not see each other's documents.
internal sealed record DocTarget(DocumentResource Resource, string ActivityId, string Agent, Guid? Registration, string Id)
{
    public static DocTarget New(DocumentResource resource, Guid? registration = null) => new(
        resource,
        StatementApi.NewIri(),
        new JsonObject { ["mbox"] = StatementApi.NewMbox() }.ToJsonString(),
        resource == DocumentResource.State ? registration : null,
        "doc-" + Guid.NewGuid().ToString("N"));

    public string Path => Resource switch
    {
        DocumentResource.State => "/xapi/activities/state",
        DocumentResource.ActivityProfile => "/xapi/activities/profile",
        _ => "/xapi/agents/profile",
    };

    public string IdName => Resource == DocumentResource.State ? "stateId" : "profileId";

    public string Url => ListUrl() + $"&{IdName}={Uri.EscapeDataString(Id)}";

    public DocTarget WithId(string id) => this with { Id = id };

    public string ListUrl(string? since = null)
    {
        var query = new List<string>();
        if (Resource != DocumentResource.AgentProfile)
        {
            query.Add("activityId=" + Uri.EscapeDataString(ActivityId));
        }

        if (Resource != DocumentResource.ActivityProfile)
        {
            query.Add("agent=" + Uri.EscapeDataString(Agent));
        }

        if (Registration is { } registration)
        {
            query.Add("registration=" + registration);
        }

        if (since is not null)
        {
            query.Add("since=" + Uri.EscapeDataString(since));
        }

        return Path + "?" + string.Join('&', query);
    }
}

internal static class DocumentApi
{
    public const string JsonType = "application/json";

    public static IEnumerable<TheoryDataRow<DocumentResource, TestDatabase>> ResourcesAndDatabases()
    {
        foreach (var database in new[] { TestDatabase.Sqlite, TestDatabase.PostgreSql })
        {
            foreach (var resource in Enum.GetValues<DocumentResource>())
            {
                yield return new(resource, database)
                {
                    Skip = database == TestDatabase.PostgreSql && !TestDatabases.PostgreSqlEnabled
                        ? $"PostgreSQL tests need Docker: they run on Linux, or when {TestDatabases.PostgreSqlSwitch}=1"
                        : null,
                };
            }
        }
    }

    public static Task<HttpResponseMessage> SendAsync(
        this HttpClient client, HttpMethod method, string url, string? body = null, string? contentType = JsonType,
        string? ifMatch = null, string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            if (contentType is not null)
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> PutDocAsync(this HttpClient client, DocTarget target, string body, string? contentType = JsonType, string? ifMatch = null, string? ifNoneMatch = null) =>
        client.SendAsync(HttpMethod.Put, target.Url, body, contentType, ifMatch, ifNoneMatch);

    public static Task<HttpResponseMessage> PostDocAsync(this HttpClient client, DocTarget target, string body, string? contentType = JsonType, string? ifMatch = null, string? ifNoneMatch = null) =>
        client.SendAsync(HttpMethod.Post, target.Url, body, contentType, ifMatch, ifNoneMatch);

    public static Task<HttpResponseMessage> DeleteDocAsync(this HttpClient client, DocTarget target, string? ifMatch = null, string? ifNoneMatch = null) =>
        client.SendAsync(HttpMethod.Delete, target.Url, ifMatch: ifMatch, ifNoneMatch: ifNoneMatch);

    public static Task<HttpResponseMessage> GetDocAsync(this HttpClient client, DocTarget target) =>
        client.SendAsync(HttpMethod.Get, target.Url);

    // Creates the document the way each version allows without knowing whether it exists
    public static async Task<string> CreateAsync(this HttpClient client, DocTarget target, string body = """{"a":1}""", string? contentType = JsonType)
    {
        using var response = await client.PutDocAsync(target, body, contentType, ifNoneMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.ETag!.ToString();
    }

    public static async Task<(string Body, string ETag, string ContentType)> ReadAsync(this HttpClient client, DocTarget target)
    {
        using var response = await client.GetDocAsync(target);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (body, response.Headers.ETag!.ToString(), response.Content.Headers.ContentType!.ToString());
    }

    public static async Task<string[]> ListAsync(this HttpClient client, DocTarget target, string? since = null)
    {
        using var response = await client.SendAsync(HttpMethod.Get, target.ListUrl(since));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await StatementApi.ReadArrayAsync(response)).Select(n => n!.GetValue<string>())];
    }

    public static EntityTagHeaderValue? ETagOf(HttpResponseMessage response) => response.Headers.ETag;
}
