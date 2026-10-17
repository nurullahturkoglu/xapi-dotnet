using System.Net;
using System.Net.Http.Headers;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class ErrorTests
{
    [Fact]
    public async Task Conflict_throws_XapiConflictException_with_the_body()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.Conflict, "{\"error\":\"different statement\"}");

        var exception = await Assert.ThrowsAsync<XapiConflictException>(() => client.SaveStatementAsync(Samples.Statement(Samples.Id(1)), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("{\"error\":\"different statement\"}", exception.ResponseBody);
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task Precondition_failed_throws_XapiPreconditionFailedException()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.PreconditionFailed, "etag mismatch", "text/plain");
        var key = new StateKey(Samples.ActivityId, Samples.Learner, "bookmark");

        var exception = await Assert.ThrowsAsync<XapiPreconditionFailedException>(
            () => client.PutDocumentAsync(key, new XapiDocument(new byte[] { 1 }, "application/octet-stream"), DocumentPrecondition.IfMatch("\"old\""), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Equal("etag mismatch", exception.ResponseBody);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, true)]
    [InlineData(413, false)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(505, false)]
    public async Task Other_errors_throw_XapiHttpException_with_status_and_transience(int status, bool transient)
    {
        var (client, handler) = TestClient.Create();
        handler.Respond((HttpStatusCode)status, "problem", "text/plain");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));

        Assert.IsType<XapiHttpException>(exception);
        Assert.Equal((HttpStatusCode)status, exception.StatusCode);
        Assert.Equal("problem", exception.ResponseBody);
        Assert.Equal(transient, exception.IsTransient);
    }

    [Fact]
    public async Task Message_names_method_and_path_but_not_the_query()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.Forbidden, "not allowed", "text/plain");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.GetPersonAsync(Samples.Learner, cancellationToken: Ct));

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.Contains("GET /xapi/agents", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not allowed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("learner", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retry_after_in_seconds_is_reported()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond((HttpStatusCode)429, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));

        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task Retry_after_as_a_date_is_converted_to_a_delay_from_the_response_date()
    {
        var (client, handler) = TestClient.Create();
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        handler.Respond(HttpStatusCode.ServiceUnavailable, configure: r =>
        {
            r.Headers.Date = now;
            r.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(2));
        });

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));

        Assert.Equal(TimeSpan.FromMinutes(2), exception.RetryAfter);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task No_retry_after_header_means_null()
    {
        var (client, handler) = TestClient.Create();
        handler.Respond(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));

        Assert.Null(exception.RetryAfter);
    }

    [Fact]
    public async Task Network_failures_surface_unchanged()
    {
        var (client, handler) = TestClient.Create();
        var failure = new HttpRequestException("connection refused");
        handler.Respond(_ => throw failure);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task Cancellation_surfaces_unchanged()
    {
        var (client, handler) = TestClient.Create();
        handler.Fallback = _ => FakeHandler.Response(HttpStatusCode.OK, "[]");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Malformed_success_body_throws_XapiHttpException_with_the_body()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson("not json");

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("not json", exception.ResponseBody);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Exceptions_can_be_created_for_tests_of_callers()
    {
        var transient = new XapiHttpException("busy", HttpStatusCode.ServiceUnavailable, "busy", TimeSpan.FromSeconds(5));

        Assert.True(transient.IsTransient);
        Assert.Equal(TimeSpan.FromSeconds(5), transient.RetryAfter);
        Assert.Equal(HttpStatusCode.Conflict, new XapiConflictException().StatusCode);
        Assert.Equal("{}", new XapiConflictException("conflict", "{}").ResponseBody);
        Assert.Equal(HttpStatusCode.PreconditionFailed, new XapiPreconditionFailedException("changed").StatusCode);
        Assert.False(new XapiHttpException().IsTransient);
    }
}
