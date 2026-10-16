using System.Net;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using Xapi.Json;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class StatementTests
{
    public static TheoryData<string> Versions => ["1.0.3", "2.0.0"];

    [Fact]
    public async Task Save_without_id_posts_the_statement_as_json_and_returns_the_assigned_id()
    {
        var (client, handler) = TestClient.Create();
        var assigned = Samples.Id(1);
        handler.RespondJson($"[\"{assigned}\"]");
        var statement = Samples.Statement();

        var id = await client.SaveStatementAsync(statement, cancellationToken: Ct);

        Assert.Equal(assigned, id);
        var request = handler.Single;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TestClient.Endpoint + "statements", request.Uri.AbsoluteUri);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), request.BodyText);
    }

    [Fact]
    public async Task Save_with_id_puts_to_the_statement_id_and_returns_it()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NoContent);
        var statement = Samples.Statement(Samples.Id(7));

        var id = await client.SaveStatementAsync(statement, cancellationToken: Ct);

        Assert.Equal(Samples.Id(7), id);
        var request = handler.Single;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(TestClient.Endpoint + "statements", request.Path);
        Assert.Equal([new("statementId", Samples.Id(7).ToString())], request.QueryParameters);
        Assert.Equal(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), request.BodyText);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Every_request_carries_the_configured_version_header_and_credentials(string versionText)
    {
        Assert.True(XapiVersion.TryParseHeader(versionText, out var version));
        var (client, handler) = TestClient.Create(version);
        handler.Fallback = _ => FakeHandler.Response(HttpStatusCode.OK, "[]");
        handler.Respond(HttpStatusCode.NoContent);
        handler.RespondJson($"[\"{Samples.Id(1)}\"]");
        handler.RespondJson(Samples.StoredJson(Samples.Id(1), version));
        handler.RespondJson(Samples.StatementResultJson(version, null));
        handler.Respond(HttpStatusCode.NotFound);
        handler.RespondJson("{\"objectType\":\"Person\"}");
        handler.Respond(HttpStatusCode.NotFound);
        handler.RespondJson("{\"version\":[\"1.0.3\",\"2.0.0\"]}");

        await client.SaveStatementAsync(Samples.Statement(Samples.Id(1)), cancellationToken: Ct);
        await client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct);
        await client.GetStatementAsync(Samples.Id(1), cancellationToken: Ct);
        await client.QueryStatementsAsync(new StatementQuery(), cancellationToken: Ct);
        await client.GetDocumentAsync(new StateKey(Samples.ActivityId, Samples.Learner, "bookmark"), cancellationToken: Ct);
        await client.GetPersonAsync(Samples.Learner, cancellationToken: Ct);
        await client.GetActivityAsync(Samples.ActivityId, cancellationToken: Ct);
        await client.GetAboutAsync(cancellationToken: Ct);
        await client.GetStateIdsAsync(Samples.ActivityId, Samples.Learner, cancellationToken: Ct);
        await client.DeleteStatesAsync(Samples.ActivityId, Samples.Learner, cancellationToken: Ct);

        Assert.Equal(10, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(versionText, r.Header("X-Experience-API-Version"));
            Assert.Equal("Basic a2V5OnNlY3JldA==", r.Header("Authorization"));
        });
    }

    [Fact]
    public async Task Save_batch_posts_an_array_and_returns_the_ids_in_order()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(1)}\",\"{Samples.Id(2)}\"]");
        Statement[] statements = [Samples.Statement(), Samples.Statement(Samples.Id(2))];

        var ids = await client.SaveStatementsAsync(statements, cancellationToken: Ct);

        Assert.Equal([Samples.Id(1), Samples.Id(2)], ids);
        var request = handler.Single;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(string.Empty, request.RawQuery);
        Assert.Equal(XapiJson.SerializeStatements(statements, XapiVersion.V2_0_0), request.BodyText);
    }

    [Fact]
    public async Task Save_batch_of_nothing_sends_nothing()
    {
        var (client, handler) = TestClient.Create();

        var ids = await client.SaveStatementsAsync([], cancellationToken: Ct);

        Assert.Empty(ids);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Save_batch_rejects_a_response_with_the_wrong_number_of_ids()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(1)}\"]");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementsAsync([Samples.Statement(), Samples.Statement()], cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Contains("1 Statement ids for 2 Statements", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_batch_accepts_missing_ids_of_statements_the_lrs_already_had()
    {
        // lrsql answers a batch it partly or fully stored before with the ids of the new Statements only
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(2)}\"]").RespondJson("[]");
        Statement[] statements = [Samples.Statement(Samples.Id(1)), Samples.Statement(Samples.Id(2))];

        Assert.Equal([Samples.Id(1), Samples.Id(2)], await client.SaveStatementsAsync(statements, cancellationToken: Ct));
        Assert.Equal([Samples.Id(1), Samples.Id(2)], await client.SaveStatementsAsync(statements, cancellationToken: Ct));
    }

    [Fact]
    public async Task Save_batch_rejects_an_id_that_was_not_sent()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(3)}\"]");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(
            () => client.SaveStatementsAsync([Samples.Statement(Samples.Id(1)), Samples.Statement(Samples.Id(2))], cancellationToken: Ct));

        Assert.Contains("1 Statement ids for 2 Statements", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_statement_is_rejected_before_any_request()
    {
        var (client, handler) = TestClient.Create();
        var invalid = Samples.Statement() with { Result = new Result { Score = new Score { Scaled = 2 } } };

        var exception = await Assert.ThrowsAsync<XapiValidationException>(() => client.SaveStatementAsync(invalid, cancellationToken: Ct));

        Assert.Contains(exception.Errors, e => e.Code == XapiErrorCodes.ScoreScaled);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Batch_with_a_repeated_id_is_rejected_before_any_request()
    {
        var (client, handler) = TestClient.Create();
        var statement = Samples.Statement(Samples.Id(3));

        var exception = await Assert.ThrowsAsync<XapiValidationException>(() => client.SaveStatementsAsync([statement, statement], cancellationToken: Ct));

        Assert.Contains(exception.Errors, e => e.Code == XapiErrorCodes.BatchDuplicateId);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Validation_can_be_switched_off()
    {
        var (client, handler) = TestClient.Create(configure: o => o.ValidateBeforeSend = false);
        handler.Respond(HttpStatusCode.BadRequest, "score out of range", "text/plain");
        var invalid = Samples.Statement() with { Result = new Result { Score = new Score { Scaled = 2 } } };

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(invalid, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Get_statement_sends_only_statementId_for_exact_format_and_reads_leniently()
    {
        var (client, handler) = TestClient.Create();
        var json = Samples.StoredJson(Samples.Id(5), XapiVersion.V2_0_0);
        handler.RespondJson(json.Insert(1, "\"lrsSpecific\":true,"));

        var statement = await client.GetStatementAsync(Samples.Id(5), cancellationToken: Ct);

        Assert.NotNull(statement);
        Assert.Equal(Samples.Id(5), statement.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero), statement.Stored);
        var request = handler.Single;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal([new("statementId", Samples.Id(5).ToString())], request.QueryParameters);
    }

    [Theory]
    [InlineData(StatementFormat.Ids, "ids")]
    [InlineData(StatementFormat.Canonical, "canonical")]
    public async Task Get_statement_sends_a_non_default_format(StatementFormat format, string expected)
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StoredJson(Samples.Id(5), XapiVersion.V2_0_0));

        await client.GetStatementAsync(Samples.Id(5), format, cancellationToken: Ct);

        Assert.Equal(expected, handler.Single.Query("format"));
    }

    [Fact]
    public async Task Get_statement_returns_null_on_404()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.NotFound, "no such statement", "text/plain");

        Assert.Null(await client.GetStatementAsync(Samples.Id(5), cancellationToken: Ct));
    }

    [Fact]
    public async Task Get_voided_statement_uses_voidedStatementId()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson(Samples.StoredJson(Samples.Id(5), XapiVersion.V2_0_0));

        var statement = await client.GetVoidedStatementAsync(Samples.Id(5), cancellationToken: Ct);

        Assert.NotNull(statement);
        Assert.Equal([new("voidedStatementId", Samples.Id(5).ToString())], handler.Single.QueryParameters);
    }

    [Fact]
    public async Task Get_statement_rejects_a_body_that_is_not_a_statement()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("{\"actor\":{}}");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.GetStatementAsync(Samples.Id(5), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("{\"actor\":{}}", exception.ResponseBody);
        Assert.IsType<XapiValidationException>(exception.InnerException);
    }

    [Fact]
    public async Task Void_posts_a_voided_statement_that_references_the_target()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(9)}\"]");
        var admin = Agent.FromMbox("admin@example.com");

        var id = await client.VoidStatementAsync(Samples.Id(4), admin, cancellationToken: Ct);

        Assert.Equal(Samples.Id(9), id);
        var sent = XapiJson.ParseStatement(handler.Single.BodyText, XapiVersion.V2_0_0).GetValueOrThrow();
        Assert.Equal(Verbs.VoidedId, sent.Verb.Id);
        Assert.Equal(new StatementRef(Samples.Id(4)), sent.Object);
        Assert.Equal(admin, sent.Actor);
        Assert.Equal(HttpMethod.Post, handler.Single.Method);
    }
}
