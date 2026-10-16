using System.Net;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class VersionNegotiationTests
{
    private static (XapiClient Client, FakeHandler Handler) Negotiating() =>
        TestClient.Create(configure: o => o.Version = null);

    [Fact]
    public async Task Without_a_version_the_client_asks_about_once_and_prefers_2_0()
    {
        var (client, handler) = Negotiating();
        handler.RespondJson("{\"version\":[\"1.0.3\",\"2.0.0\"]}");
        handler.Fallback = _ => FakeHandler.Response(HttpStatusCode.OK, $"[\"{Samples.Id(1)}\"]");

        await client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct);
        await client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct);

        Assert.Equal(
            ["GET about 2.0.0", "POST statements 2.0.0", "POST statements 2.0.0"],
            handler.Requests.Select(r => $"{r.Method} {r.Path.Substring(TestClient.Endpoint.Length)} {r.Header("X-Experience-API-Version")}"));
        Assert.Equal(XapiVersion.V2_0_0, await client.GetVersionAsync(cancellationToken: Ct));
    }

    [Theory]
    [InlineData("[\"1.0.3\"]", "1.0.3")]
    [InlineData("[\"1.0.0\",\"1.0.1\"]", "1.0.3")]
    [InlineData("[\"2.0.0\",\"1.0.3\"]", "2.0.0")]
    [InlineData("[\"0.95\",\"2.0\",\"2.1.0\"]", "2.0.0")]
    public async Task The_highest_mutually_supported_version_is_chosen(string versions, string expected)
    {
        var (client, handler) = Negotiating();
        handler.RespondJson($"{{\"version\":{versions}}}");

        var version = await client.GetVersionAsync(cancellationToken: Ct);

        Assert.Equal(expected, version.HeaderValue);
    }

    [Fact]
    public async Task No_common_version_fails_and_is_not_cached()
    {
        var (client, handler) = Negotiating();
        handler.RespondJson("{\"version\":[\"0.95\"]}");
        handler.RespondJson("{\"version\":[\"1.0.3\"]}");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => client.GetVersionAsync(cancellationToken: Ct));
        var retried = await client.GetVersionAsync(cancellationToken: Ct);

        Assert.Contains("0.95", exception.Message, StringComparison.Ordinal);
        Assert.Equal(XapiVersion.V1_0_3, retried);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Failed_about_request_is_not_cached()
    {
        var (client, handler) = Negotiating();
        handler.Respond(HttpStatusCode.ServiceUnavailable);
        handler.RespondJson("{\"version\":[\"2.0.0\"]}");

        await Assert.ThrowsAsync<XapiHttpException>(() => client.GetVersionAsync(cancellationToken: Ct));
        Assert.Equal(XapiVersion.V2_0_0, await client.GetVersionAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Concurrent_first_requests_share_one_negotiation()
    {
        var (client, handler) = Negotiating();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aboutCalls = 0;
        handler.FallbackAsync = async request =>
        {
            if (request.Path.EndsWith("/about", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref aboutCalls);
                await release.Task;
                return FakeHandler.Response(HttpStatusCode.OK, "{\"version\":[\"2.0.0\"]}");
            }

            return FakeHandler.Response(HttpStatusCode.NotFound);
        };

        var calls = Enumerable.Range(0, 5).Select(i => Task.Run(() => client.GetStatementAsync(Samples.Id(i), cancellationToken: Ct))).ToArray();
        await Task.Delay(100, Ct);
        release.SetResult(true);
        await Task.WhenAll(calls);

        Assert.Equal(1, aboutCalls);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task A_caller_can_stop_waiting_without_cancelling_the_negotiation_for_others()
    {
        var (client, handler) = Negotiating();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.RespondAsync(async _ =>
        {
            await release.Task;
            return FakeHandler.Response(HttpStatusCode.OK, "{\"version\":[\"2.0.0\"]}");
        });

        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Run(() => client.GetVersionAsync(cancellation.Token));
        var patient = Task.Run(() => client.GetVersionAsync(cancellationToken: Ct));
        await Task.Delay(100, Ct);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult(true);

        Assert.Equal(XapiVersion.V2_0_0, await patient);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Configured_version_never_asks_about()
    {
        var (client, handler) = TestClient.Create(XapiVersion.V1_0_3);

        Assert.Equal(XapiVersion.V1_0_3, await client.GetVersionAsync(cancellationToken: Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Get_about_does_not_negotiate_and_reads_extensions()
    {
        var (client, handler) = Negotiating();
        handler.RespondJson("{\"version\":[\"2.0.0\"],\"extensions\":{\"https://example.com/ext\":{\"a\":1}},\"vendor\":\"x\"}");

        var about = await client.GetAboutAsync(cancellationToken: Ct);

        Assert.Equal(["2.0.0"], about.Version);
        Assert.NotNull(about.Extensions);
        Assert.Single(handler.Requests);
        Assert.Equal("2.0.0", handler.Single.Header("X-Experience-API-Version"));
    }
}
