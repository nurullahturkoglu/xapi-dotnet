using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class NegotiationTests(LrsHosts hosts)
{
    private const string VersionHeader = "X-Experience-API-Version";

    [Fact]
    public async Task About_needs_no_credentials_or_version_header_and_lists_both_versions()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var response = await app.Anonymous(version: null).GetAsync("/xapi/about", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2.0.0", Header(response, VersionHeader));
        var about = await ReadObjectAsync(response);
        Assert.Equal(["1.0.3", "2.0.0"], about["version"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task Head_about_has_headers_and_no_body()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var request = new HttpRequestMessage(HttpMethod.Head, "/xapi/about");
        using var response = await app.Anonymous().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2.0.0", Header(response, VersionHeader));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("2.0.0", "2.0.0")]
    [InlineData("2.0", "2.0.0")]
    [InlineData("2.0.7", "2.0.0")]
    [InlineData("1.0.3", "1.0.3")]
    // xAPI 1.0.3, Communication 3.3: "1.0" is accepted as "1.0.0"
    [InlineData("1.0", "1.0.3")]
    [InlineData("1.0.0", "1.0.3")]
    [InlineData("1.0.1", "1.0.3")]
    [InlineData("1.0.9", "1.0.3")]
    public async Task Supported_versions_are_answered_with_the_matching_version(string requested, string answered)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var response = await app.Client(version: requested).GetStatementAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(answered, Header(response, VersionHeader));
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("2.1.0")]
    [InlineData("3.0.0")]
    [InlineData("0.95")]
    [InlineData("2.0.0-rc")]
    [InlineData("latest")]
    public async Task Other_versions_are_rejected_with_400(string requested)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var response = await app.Client(version: requested).GetStatementAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("2.0.0", Header(response, VersionHeader));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_missing_version_header_is_rejected_with_400()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var response = await app.Client(version: null).PostStatementsAsync(Statement());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("2.0.0", Header(response, VersionHeader));
    }

    [Fact]
    public async Task Version_1_0_3_can_be_turned_off()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddXapiLrs(o => o.EnableVersion1_0_3 = false));

        using var rejected = await app.Client(version: "1.0.3").GetStatementAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var about = await app.Anonymous().GetAsync("/xapi/about", TestContext.Current.CancellationToken);
        Assert.Equal(["2.0.0"], (await ReadObjectAsync(about))["version"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task Statement_resource_responses_carry_consistent_through()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var response = await app.Client().GetStatementAsync(Guid.NewGuid());

        var value = Header(response, "X-Experience-API-Consistent-Through");
        Assert.NotNull(value);
        Assert.InRange(DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture), before, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Unsupported_methods_get_405_with_allow_and_unknown_resources_404()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client();

        using var delete = await client.DeleteAsync("/xapi/statements", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(["GET", "HEAD", "POST", "PUT"], delete.Content.Headers.Allow.Order(StringComparer.Ordinal));
        Assert.Equal("2.0.0", Header(delete, VersionHeader));

        using var unknown = await client.GetAsync("/xapi/nothing-here", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData("statementid")]
    [InlineData("statementId&statementId")]
    [InlineData("unknown")]
    public async Task Unknown_wrong_case_or_repeated_parameters_are_rejected(string parameters)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var query = string.Join('&', parameters.Split('&').Select(p => $"{p}={Guid.NewGuid()}"));
        using var response = await app.Client().GetAsync($"/xapi/statements?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
