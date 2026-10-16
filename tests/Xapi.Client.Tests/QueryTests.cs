using System.Net;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using Xapi.Json;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class QueryTests
{
    [Fact]
    public async Task Default_query_sends_no_parameters()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, null));

        var result = await client.QueryStatementsAsync(new StatementQuery(), cancellationToken: Ct);

        Assert.Empty(result.Statements);
        Assert.Null(result.More);
        Assert.Empty(result.Attachments);
        var request = handler.Single;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(TestClient.Endpoint + "statements", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Every_filter_is_sent_with_its_standard_name_and_encoding()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, null));
        var registration = Guid.Parse("4d3b2a1c-0000-4000-8000-00000000abcd");
        var query = new StatementQuery
        {
            Agent = Agent.FromMbox("first+last@example.com"),
            Verb = Verbs.Completed.Id,
            Activity = new Iri("https://example.com/activities/a?b=c&d=e"),
            Registration = registration,
            RelatedActivities = true,
            RelatedAgents = true,
            Since = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(3)),
            Until = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            Limit = 25,
            Format = StatementFormat.Canonical,
            Attachments = false,
            Ascending = true,
        };

        await client.QueryStatementsAsync(query, cancellationToken: Ct);

        var request = handler.Single;
        Assert.Equal(
            [
                new("agent", "{\"objectType\":\"Agent\",\"mbox\":\"mailto:first+last@example.com\"}"),
                new("verb", "http://adlnet.gov/expapi/verbs/completed"),
                new("activity", "https://example.com/activities/a?b=c&d=e"),
                new("registration", "4d3b2a1c-0000-4000-8000-00000000abcd"),
                new("related_activities", "true"),
                new("related_agents", "true"),
                new("since", "2026-10-01T09:00:00.000Z"),
                new("until", "2026-10-02T00:00:00.000Z"),
                new("limit", "25"),
                new("format", "canonical"),
                new("ascending", "true"),
            ],
            request.QueryParameters);

        // A raw "+" would be read as a space by the LRS, and a raw "&" would split the parameter
        Assert.Contains("first%2Blast%40example.com", request.RawQuery, StringComparison.Ordinal);
        Assert.Contains("activity=https%3A%2F%2Fexample.com%2Factivities%2Fa%3Fb%3Dc%26d%3De&", request.RawQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attachments_flag_is_sent_as_a_json_boolean()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, null));

        await client.QueryStatementsAsync(new StatementQuery { Attachments = true }, cancellationToken: Ct);

        Assert.Equal("attachments=true", handler.Single.RawQuery);
    }

    [Fact]
    public async Task Negative_limit_is_rejected()
    {
        var (client, _) = TestClient.Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.QueryStatementsAsync(new StatementQuery { Limit = -1 }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Result_carries_statements_more_and_consistent_through()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(
            HttpStatusCode.OK,
            Samples.StatementResultJson(XapiVersion.V2_0_0, "/xapi/statements?more=abc", Samples.Id(1), Samples.Id(2)),
            configure: r => r.Headers.TryAddWithoutValidation("X-Experience-API-Consistent-Through", "2026-10-17T08:00:01.250Z"));

        var result = await client.QueryStatementsAsync(new StatementQuery(), cancellationToken: Ct);

        Assert.Equal([Samples.Id(1), Samples.Id(2)], result.Statements.Select(s => s.Id!.Value));
        Assert.Equal("/xapi/statements?more=abc", result.More?.OriginalString);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 8, 0, 1, 250, TimeSpan.Zero), result.ConsistentThrough);
    }

    [Fact]
    public async Task Empty_more_means_last_page()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, string.Empty));

        var result = await client.QueryStatementsAsync(new StatementQuery(), cancellationToken: Ct);

        Assert.Null(result.More);
    }

    [Fact]
    public async Task Body_that_is_not_a_statement_result_is_rejected()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("[]");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.QueryStatementsAsync(new StatementQuery(), cancellationToken: Ct));

        Assert.Contains("not a StatementResult", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/xapi/statements?more=abc%2Fdef", "https://lrs.example.com/xapi/statements?more=abc%2Fdef")]
    [InlineData("statements?more=abc", "https://lrs.example.com/xapi/statements?more=abc")]
    [InlineData("http://internal-host:8080/xapi/statements?more=abc", "https://lrs.example.com/xapi/statements?more=abc")]
    public async Task More_keeps_the_endpoint_origin(string more, string expected)
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, null));

        await client.GetMoreStatementsAsync(new Uri(more, UriKind.RelativeOrAbsolute), cancellationToken: Ct);

        Assert.Equal(expected, handler.Single.Uri.AbsoluteUri);
        Assert.Equal("Basic a2V5OnNlY3JldA==", handler.Single.Header("Authorization"));
    }

    [Fact]
    public async Task More_that_leaves_the_host_is_rejected_without_a_request()
    {
        var (client, handler) = TestClient.Create();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMoreStatementsAsync(new Uri("//attacker.example/steal", UriKind.Relative), cancellationToken: Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Query_all_follows_more_until_the_last_page()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, "/xapi/statements?more=2", Samples.Id(1), Samples.Id(2)));
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, "/xapi/statements?more=3"));
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, null, Samples.Id(3)));

        var ids = new List<Guid>();
        await foreach (var statement in client.QueryAllStatementsAsync(new StatementQuery { Limit = 2, Attachments = true }, cancellationToken: Ct))
        {
            ids.Add(statement.Id!.Value);
        }

        Assert.Equal([Samples.Id(1), Samples.Id(2), Samples.Id(3)], ids);
        Assert.Equal(
            ["limit=2", "more=2", "more=3"],
            handler.Requests.Select(r => r.RawQuery));
    }

    [Fact]
    public async Task Query_all_stops_requesting_when_the_caller_stops()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, "/xapi/statements?more=2", Samples.Id(1), Samples.Id(2)));

        await foreach (var statement in client.QueryAllStatementsAsync(new StatementQuery(), cancellationToken: Ct))
        {
            break;
        }

        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Query_all_rejects_a_null_query_at_the_call()
    {
        var (client, handler) = TestClient.Create();

        Assert.Throws<ArgumentNullException>(() => client.QueryAllStatementsAsync(null!, cancellationToken: Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Query_all_honours_the_token_given_to_with_cancellation()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StatementResultJson(XapiVersion.V2_0_0, "/xapi/statements?more=2", Samples.Id(1)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var statement in client.QueryAllStatementsAsync(new StatementQuery(), CancellationToken.None).WithCancellation(cancellation.Token))
            {
                cancellation.Cancel();
            }
        });

        Assert.Single(handler.Requests);
    }
}
