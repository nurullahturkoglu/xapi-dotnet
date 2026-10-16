using System.Net;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

/// <summary>Agents, Activities, credentials and endpoint handling.</summary>
public class ResourceTests
{
    [Fact]
    public async Task Get_person_sends_the_agent_and_reads_the_person()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("{\"objectType\":\"Person\",\"name\":[\"Learner\",\"L. Earner\"],\"mbox\":[\"mailto:learner@example.com\"]}");

        var person = await client.GetPersonAsync(Samples.Learner, cancellationToken: Ct);

        Assert.Equal(["Learner", "L. Earner"], person.Names);
        Assert.Equal([Mbox.FromEmail("learner@example.com")], person.Mboxes);
        Assert.Equal(TestClient.Endpoint + "agents", handler.Single.Path);
        Assert.Equal("{\"objectType\":\"Agent\",\"name\":\"Learner\",\"mbox\":\"mailto:learner@example.com\"}", handler.Single.Query("agent"));
    }

    [Fact]
    public async Task Get_activity_reads_the_definition()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"{{\"objectType\":\"Activity\",\"id\":\"{Samples.ActivityId.Value}\",\"definition\":{{\"name\":{{\"en\":\"Course\"}}}}}}");

        var activity = await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.NotNull(activity);
        Assert.Equal("Course", activity.Definition?.Name?["en"]);
        Assert.Equal(TestClient.Endpoint + "activities", handler.Single.Path);
        Assert.Equal(Samples.ActivityId.Value, handler.Single.Query("activityId"));
    }

    [Fact]
    public async Task Get_activity_returns_null_on_404()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct));
    }

    [Fact]
    public async Task Bearer_token_is_asked_for_on_every_request()
    {
        var calls = 0;
        var (client, handler) = TestClient.Create(configure: o => o.Credentials = XapiCredentials.Bearer(_ => new ValueTask<string>($"token-{++calls}")));
        handler.Fallback = _ => FakeHandler.Response(HttpStatusCode.NotFound);

        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);
        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.Equal(["Bearer token-1", "Bearer token-2"], handler.Requests.Select(r => r.Header("Authorization")));
    }

    [Fact]
    public async Task Basic_credentials_are_utf8()
    {
        var (client, handler) = TestClient.Create(configure: o => o.Credentials = XapiCredentials.Basic("kullanıcı", "şifre"));
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.Equal("Basic a3VsbGFuxLFjxLE6xZ9pZnJl", handler.Single.Header("Authorization"));
    }

    [Fact]
    public async Task Without_credentials_no_authorization_header_is_sent()
    {
        var (client, handler) = TestClient.Create(configure: o => o.Credentials = null);
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.Null(handler.Single.Header("Authorization"));
    }

    [Theory]
    [InlineData("https://lrs.example.com/xapi")]
    [InlineData("https://lrs.example.com/xapi/")]
    public async Task Endpoint_without_trailing_slash_keeps_its_last_segment(string endpoint)
    {
        var (client, handler) = TestClient.Create(configure: o => o.Endpoint = new Uri(endpoint));
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);

        Assert.Equal("https://lrs.example.com/xapi/activities", handler.Single.Path);
    }

    [Fact]
    public void Endpoint_is_required_and_must_be_absolute_http()
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(() => new XapiClient(http, new XapiClientOptions()));
        Assert.Throws<ArgumentException>(() => new XapiClient(http, new XapiClientOptions { Endpoint = new Uri("/xapi/", UriKind.Relative) }));
        Assert.Throws<ArgumentException>(() => new XapiClient(http, new XapiClientOptions { Endpoint = new Uri("ftp://lrs.example.com/xapi/") }));
    }
}
