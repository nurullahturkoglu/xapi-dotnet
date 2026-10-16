using System.Net;
using System.Text;
using System.Text.Json;

namespace Xapi.Client.IntegrationTests;

public class DocumentTests(LrsqlFixture lrs)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task State_lifecycle_honours_etag_preconditions(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var key = new StateKey(Data.NewActivityId(), Data.NewAgent(), "bookmark", Guid.NewGuid());

        Assert.Null(await client.GetDocumentAsync(key, Ct));
        await client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"page":1}""")), DocumentPrecondition.IfNoneMatchAny, Ct);
        await Assert.ThrowsAsync<XapiPreconditionFailedException>(
            () => client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"page":9}""")), DocumentPrecondition.IfNoneMatchAny, Ct));

        var first = await client.GetDocumentAsync(key, Ct);
        Assert.NotNull(first);
        Assert.NotNull(first.ETag);
        Assert.NotNull(first.LastModified);
        Assert.Equal(1, first.ToJson().GetProperty("page").GetInt32());

        await client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"page":2}""")), DocumentPrecondition.IfMatch(first.ETag), Ct);
        await Assert.ThrowsAsync<XapiPreconditionFailedException>(
            () => client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"page":3}""")), DocumentPrecondition.IfMatch(first.ETag), Ct));

        var second = await client.GetDocumentAsync(key, Ct);
        Assert.NotNull(second);
        Assert.NotEqual(first.ETag, second.ETag);
        await client.MergeDocumentAsync(key, Json("""{"chapter":"two"}"""), DocumentPrecondition.IfMatch(second.ETag!), Ct);

        var merged = await client.GetDocumentAsync(key, Ct);
        Assert.NotNull(merged);
        Assert.Equal(2, merged.ToJson().GetProperty("page").GetInt32());
        Assert.Equal("two", merged.ToJson().GetProperty("chapter").GetString());

        await Assert.ThrowsAsync<XapiPreconditionFailedException>(
            () => client.DeleteDocumentAsync(key, DocumentPrecondition.IfMatch(second.ETag!), Ct));
        await client.DeleteDocumentAsync(key, DocumentPrecondition.IfMatch(merged.ETag!), Ct);
        Assert.Null(await client.GetDocumentAsync(key, Ct));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Binary_state_keeps_its_bytes_and_content_type(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var key = new StateKey(Data.NewActivityId(), Data.NewAgent(), "blob");
        byte[] bytes = [0, 1, 2, 0xFE, 0xFF, 13, 10];

        await client.PutDocumentAsync(key, new XapiDocument(bytes, "application/octet-stream"), cancellationToken: Ct);
        var stored = await client.GetDocumentAsync(key, Ct);

        Assert.NotNull(stored);
        Assert.Equal(bytes, stored.Content.ToArray());
        Assert.StartsWith("application/octet-stream", stored.ContentType, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task State_ids_filter_by_since_and_delete_states_clears_them(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var activityId = Data.NewActivityId();
        var agent = Data.NewAgent();
        var registration = Guid.NewGuid();
        var text = new XapiDocument(Encoding.UTF8.GetBytes("hello"), "text/plain");

        await client.PutDocumentAsync(new StateKey(activityId, agent, "a", registration), text, cancellationToken: Ct);
        var a = await client.GetDocumentAsync(new StateKey(activityId, agent, "a", registration), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        await client.PutDocumentAsync(new StateKey(activityId, agent, "b", registration), text, cancellationToken: Ct);
        await client.PutDocumentAsync(new StateKey(activityId, agent, "other"), text, cancellationToken: Ct);

        Assert.Equal(["a", "b"], (await client.GetStateIdsAsync(activityId, agent, registration, cancellationToken: Ct)).Order(StringComparer.Ordinal));
        Assert.Equal(["b"], await client.GetStateIdsAsync(activityId, agent, registration, a!.LastModified, Ct));

        await client.DeleteStatesAsync(activityId, agent, registration, Ct);

        Assert.Empty(await client.GetStateIdsAsync(activityId, agent, registration, cancellationToken: Ct));
        Assert.Equal(["other"], await client.GetStateIdsAsync(activityId, agent, cancellationToken: Ct));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Activity_profile_round_trips_and_lists(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var activityId = Data.NewActivityId();
        var key = new ActivityProfileKey(activityId, "settings");

        await client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"theme":"dark"}""")), DocumentPrecondition.IfNoneMatchAny, Ct);
        var stored = await client.GetDocumentAsync(key, Ct);

        Assert.NotNull(stored);
        Assert.Equal("dark", stored.ToJson().GetProperty("theme").GetString());
        Assert.Equal(["settings"], await client.GetActivityProfileIdsAsync(activityId, cancellationToken: Ct));

        await client.DeleteDocumentAsync(key, DocumentPrecondition.IfMatch(stored.ETag!), Ct);
        Assert.Empty(await client.GetActivityProfileIdsAsync(activityId, cancellationToken: Ct));
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Agent_profile_round_trips_and_lists(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var agent = Data.NewAgent();
        var key = new AgentProfileKey(agent, "preferences");

        await client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"language":"tr"}""")), DocumentPrecondition.IfNoneMatchAny, Ct);
        var stored = await client.GetDocumentAsync(key, Ct);

        Assert.NotNull(stored);
        Assert.Equal("tr", stored.ToJson().GetProperty("language").GetString());
        Assert.Equal(["preferences"], await client.GetAgentProfileIdsAsync(agent, cancellationToken: Ct));
    }

    [Fact]
    public async Task Profile_put_over_an_existing_document_without_precondition_conflicts_in_2_0()
    {
        var client = lrs.CreateClient(XapiVersion.V2_0_0);
        var key = new AgentProfileKey(Data.NewAgent(), "preferences");
        await client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"language":"tr"}""")), cancellationToken: Ct);

        var conflict = await Assert.ThrowsAsync<XapiConflictException>(
            () => client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"language":"en"}""")), cancellationToken: Ct));

        Assert.Contains("If-Match", conflict.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Profile_put_without_precondition_is_rejected_by_lrsql_in_1_0_3()
    {
        // lrsql answers 400, not 409, when a 1.0.3 profile PUT has neither If-Match nor If-None-Match, even for a new
        // document. The client passes the status through instead of guessing what the LRS meant.
        var client = lrs.CreateClient(XapiVersion.V1_0_3);
        var key = new AgentProfileKey(Data.NewAgent(), "preferences");

        var error = await Assert.ThrowsAsync<XapiHttpException>(
            () => client.PutDocumentAsync(key, XapiDocument.FromJson(Json("""{"language":"tr"}""")), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.False(error.IsTransient);
    }
}
