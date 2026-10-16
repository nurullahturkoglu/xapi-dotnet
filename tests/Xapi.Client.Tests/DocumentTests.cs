using System.Net;
using System.Text;
using System.Text.Json;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class DocumentTests
{
    private const string AgentJson = "{\"objectType\":\"Agent\",\"name\":\"Learner\",\"mbox\":\"mailto:learner@example.com\"}";

    private static readonly Guid Registration = Guid.Parse("11111111-2222-4333-8444-555555555555");

    public static TheoryData<DocumentKey, string, string[]> Keys => new()
    {
        {
            new StateKey(Samples.ActivityId, Samples.Learner, "bookmark", Registration),
            "activities/state",
            ["activityId=" + Samples.ActivityId.Value, "agent=" + AgentJson, "registration=" + Registration, "stateId=bookmark"]
        },
        {
            new StateKey(Samples.ActivityId, Samples.Learner, "bookmark"),
            "activities/state",
            ["activityId=" + Samples.ActivityId.Value, "agent=" + AgentJson, "stateId=bookmark"]
        },
        {
            new ActivityProfileKey(Samples.ActivityId, "settings"),
            "activities/profile",
            ["activityId=" + Samples.ActivityId.Value, "profileId=settings"]
        },
        {
            new AgentProfileKey(Samples.Learner, "preferences"),
            "agents/profile",
            ["agent=" + AgentJson, "profileId=preferences"]
        },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Get_sends_the_key_as_parameters_and_returns_content_etag_and_last_modified(DocumentKey key, string resource, string[] parameters)
    {
        var (client, handler) = TestClient.Create();
        var lastModified = new DateTimeOffset(2026, 10, 17, 8, 30, 0, TimeSpan.Zero);
        handler.Respond(HttpStatusCode.OK, "{\"page\":3}", "application/json; charset=utf-8", r =>
        {
            r.Headers.TryAddWithoutValidation("ETag", "\"abc123\"");
            r.Content.Headers.LastModified = lastModified;
        });

        var document = await client.GetDocumentAsync(key, cancellationToken: Ct);

        Assert.NotNull(document);
        Assert.Equal("{\"page\":3}", Encoding.UTF8.GetString(document.Content.ToArray()));
        Assert.Equal("application/json; charset=utf-8", document.ContentType);
        Assert.Equal("\"abc123\"", document.ETag);
        Assert.Equal(lastModified, document.LastModified);
        Assert.Equal(3, document.ToJson().GetProperty("page").GetInt32());

        var request = handler.Single;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(TestClient.Endpoint + resource, request.Path);
        Assert.Equal(parameters, request.QueryParameters.Select(p => $"{p.Key}={p.Value}"));
    }

    [Fact]
    public async Task Get_reads_an_iso_8601_last_modified()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.OK, "{}", "application/json", r =>
            r.Content.Headers.TryAddWithoutValidation("Last-Modified", "2026-10-16T22:02:37.627000000Z"));

        var document = await client.GetDocumentAsync(new ActivityProfileKey(Samples.ActivityId, "settings"), cancellationToken: Ct);

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 22, 2, 37, 627, TimeSpan.Zero), document?.LastModified);
    }

    [Fact]
    public async Task Get_returns_null_on_404()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await client.GetDocumentAsync(new ActivityProfileKey(Samples.ActivityId, "missing"), cancellationToken: Ct));
    }

    [Fact]
    public async Task Put_sends_the_bytes_and_content_type_without_a_precondition_by_default()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);
        byte[] bytes = [0, 1, 2, 0xFF, 13, 10];

        await client.PutDocumentAsync(new AgentProfileKey(Samples.Learner, "avatar"), new XapiDocument(bytes, "image/png") { ETag = "\"ignored\"" }, cancellationToken: Ct);

        var request = handler.Single;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(bytes, request.Body);
        Assert.Equal("image/png", request.ContentType);
        Assert.Null(request.Header("If-Match"));
        Assert.Null(request.Header("If-None-Match"));
    }

    [Fact]
    public async Task If_match_sends_the_etag_exactly_as_given()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);

        await client.PutDocumentAsync(
            new StateKey(Samples.ActivityId, Samples.Learner, "bookmark"),
            XapiDocument.FromJson(JsonDocument.Parse("{\"page\":4}").RootElement),
            DocumentPrecondition.IfMatch("\"abc123\""), cancellationToken: Ct);

        var request = handler.Single;
        Assert.Equal("\"abc123\"", request.Header("If-Match"));
        Assert.Null(request.Header("If-None-Match"));
        Assert.Equal("{\"page\":4}", request.BodyText);
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public async Task If_none_match_any_sends_a_star()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);

        await client.PutDocumentAsync(
            new ActivityProfileKey(Samples.ActivityId, "settings"),
            new XapiDocument(Encoding.UTF8.GetBytes("text"), "text/plain"),
            DocumentPrecondition.IfNoneMatchAny, cancellationToken: Ct);

        Assert.Equal("*", handler.Single.Header("If-None-Match"));
        Assert.Null(handler.Single.Header("If-Match"));
    }

    [Fact]
    public async Task Merge_posts_a_json_object()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);
        using var json = JsonDocument.Parse("{ \"score\" : 5, \"name\": \"Ünal\" }");

        await client.MergeDocumentAsync(new StateKey(Samples.ActivityId, Samples.Learner, "progress"), json.RootElement, DocumentPrecondition.IfMatch("\"v1\""), cancellationToken: Ct);

        var request = handler.Single;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"score\":5,\"name\":\"Ünal\"}", request.BodyText);
        Assert.Equal("\"v1\"", request.Header("If-Match"));
    }

    [Fact]
    public async Task Merge_rejects_anything_but_an_object()
    {
        var (client, handler) = TestClient.Create();
        using var json = JsonDocument.Parse("[1]");

        await Assert.ThrowsAsync<ArgumentException>(() => client.MergeDocumentAsync(new ActivityProfileKey(Samples.ActivityId, "p"), json.RootElement, cancellationToken: Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_sends_the_precondition()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);

        await client.DeleteDocumentAsync(new AgentProfileKey(Samples.Learner, "preferences"), DocumentPrecondition.IfMatch("\"v2\""), cancellationToken: Ct);

        var request = handler.Single;
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(TestClient.Endpoint + "agents/profile", request.Path);
        Assert.Equal("\"v2\"", request.Header("If-Match"));
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task Delete_states_sends_no_state_id()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);

        await client.DeleteStatesAsync(Samples.ActivityId, Samples.Learner, Registration, cancellationToken: Ct);

        var request = handler.Single;
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(TestClient.Endpoint + "activities/state", request.Path);
        Assert.Equal(["activityId", "agent", "registration"], request.QueryParameters.Select(p => p.Key));
    }

    [Fact]
    public async Task Put_conflict_maps_to_XapiConflictException()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.Conflict, "use If-Match", "text/plain");

        await Assert.ThrowsAsync<XapiConflictException>(
            () => client.PutDocumentAsync(new StateKey(Samples.ActivityId, Samples.Learner, "s"), new XapiDocument(new byte[] { 1 }, "application/octet-stream"), cancellationToken: Ct));
    }

    [Fact]
    public async Task State_ids_are_listed_with_since()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("[\"a\",\"b\"]");

        var ids = await client.GetStateIdsAsync(Samples.ActivityId, Samples.Learner, Registration, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken: Ct);

        Assert.Equal(["a", "b"], ids);
        var request = handler.Single;
        Assert.Equal(TestClient.Endpoint + "activities/state", request.Path);
        Assert.Equal(
            ["activityId=" + Samples.ActivityId.Value, "agent=" + AgentJson, "registration=" + Registration, "since=2026-10-01T00:00:00.000Z"],
            request.QueryParameters.Select(p => $"{p.Key}={p.Value}"));
    }

    [Fact]
    public async Task Activity_profile_ids_are_listed()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("[]");

        var ids = await client.GetActivityProfileIdsAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.Empty(ids);
        Assert.Equal(TestClient.Endpoint + "activities/profile", handler.Single.Path);
        Assert.Equal(["activityId=" + Samples.ActivityId.Value], handler.Single.QueryParameters.Select(p => $"{p.Key}={p.Value}"));
    }

    [Fact]
    public async Task Agent_profile_ids_are_listed_with_since()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("[\"preferences\"]");

        var ids = await client.GetAgentProfileIdsAsync(Samples.Learner, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken: Ct);

        Assert.Equal(["preferences"], ids);
        Assert.Equal(TestClient.Endpoint + "agents/profile", handler.Single.Path);
        Assert.Equal(["agent", "since"], handler.Single.QueryParameters.Select(p => p.Key));
    }

    [Fact]
    public async Task Ids_response_that_is_not_an_array_of_strings_is_rejected()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("[\"a\",1]");

        await Assert.ThrowsAsync<XapiHttpException>(() => client.GetActivityProfileIdsAsync(Samples.ActivityId, cancellationToken: Ct));
    }

    [Fact]
    public void Document_equality_compares_bytes()
    {
        var a = new XapiDocument(new byte[] { 1, 2, 3 }, "application/octet-stream") { ETag = "\"x\"" };
        var b = new XapiDocument(new byte[] { 1, 2, 3 }, "application/octet-stream") { ETag = "\"x\"" };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Content = new byte[] { 1, 2, 4 } });
        Assert.NotEqual(a, b with { ETag = "\"y\"" });
    }

    [Fact]
    public void Keys_reject_missing_parts()
    {
        Assert.Throws<ArgumentException>(() => new StateKey(default, Samples.Learner, "s"));
        Assert.Throws<ArgumentNullException>(() => new StateKey(Samples.ActivityId, null!, "s"));
        Assert.Throws<ArgumentException>(() => new ActivityProfileKey(Samples.ActivityId, ""));
        Assert.Throws<ArgumentNullException>(() => new AgentProfileKey(Samples.Learner, null!));
        Assert.Throws<ArgumentException>(() => DocumentPrecondition.IfMatch(""));
    }
}
