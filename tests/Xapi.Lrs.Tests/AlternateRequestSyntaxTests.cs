using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;
using static Xapi.Lrs.Tests.Infrastructure.QueryApi;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// xAPI 1.0.3, Communication 1.3: POST with ?method=X and everything else in a form body. Not part of xAPI 2.0.
public class AlternateRequestSyntaxTests(LrsHosts hosts)
{
    private static readonly string Authorization = Basic(Credentials.All, Credentials.SecretOf(Credentials.All)).ToString();

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_put_sent_as_a_form_post_stores_the_statement(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var id = Guid.NewGuid();

        // Headers as form fields, no Content-Type field: the content is JSON
        using var response = await PostFormAsync(app.Anonymous(version: null), "PUT", new()
        {
            ["statementId"] = id.ToString(),
            ["content"] = Statement().ToJsonString(),
            ["Authorization"] = Authorization,
            ["X-Experience-API-Version"] = "1.0.3",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("1.0.3", Header(response, "X-Experience-API-Version"));
        Assert.NotNull(Header(response, "X-Experience-API-Consistent-Through"));
        var stored = await app.Client(version: "1.0.3").GetStoredAsync(id);
        Assert.Equal("1.0.0", stored["version"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_get_sent_as_a_form_post_runs_the_query(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var verb = NewIri("verbs");
        var ids = await app.Client().PutAllAsync(Statement(verb: verb), Statement(verb: verb));

        // Real headers work too; the parameters are form fields
        using var response = await PostFormAsync(app.Client(version: "1.0.3"), "GET", new() { ["verb"] = verb, ["limit"] = "1" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadObjectAsync(response);
        Assert.Equal([ids[1]], Ids(result));
        Assert.Equal([ids[0]], Ids(await app.Client(version: "1.0.3").MoreAsync(result)));
    }

    [Fact]
    public async Task A_post_sent_as_a_form_post_stores_a_batch_with_a_content_type_field()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var id = Guid.NewGuid();

        using var response = await PostFormAsync(app.Client(version: "1.0.3"), "POST", new()
        {
            ["content"] = new System.Text.Json.Nodes.JsonArray(Statement(id)).ToJsonString(),
            ["Content-Type"] = "application/json",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([id.ToString()], (await ReadArrayAsync(response)).Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task Form_credentials_are_authenticated_like_the_header()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);

        using var response = await PostFormAsync(app.Anonymous(version: "1.0.3"), "GET", new()
        {
            ["Authorization"] = Basic(Credentials.All, "wrong").ToString(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_alternate_requests_are_rejected_with_400()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Client(version: "1.0.3");
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var statement = Statement().ToJsonString();

        // Other query parameters than method (1.3.s3.b3)
        await AssertBadRequestAsync(await client.PostAsync(
            $"/xapi/statements?method=PUT&statementId={id}",
            Form(new() { ["statementId"] = id.ToString(), ["content"] = statement }),
            ct));

        // A form field overrides the version header (1.3.s3.b7)
        await AssertBadRequestAsync(await PostFormAsync(client, "PUT", new()
        {
            ["statementId"] = id.ToString(),
            ["content"] = statement,
            ["X-Experience-API-Version"] = "0.8",
        }));

        // Not a form, and not a POST
        await AssertBadRequestAsync(await client.PostAsync(
            "/xapi/statements?method=PUT",
            new StringContent($$"""{"statementId":"{{id}}","content":{{statement}}}""", Encoding.UTF8, "application/x-www-form-urlencoded"),
            ct));
        await AssertBadRequestAsync(await client.PutAsync($"/xapi/statements?method=POST", Form(new() { ["content"] = statement }), ct));

        // No content, unknown method, misspelled field
        await AssertBadRequestAsync(await client.PostAsync("/xapi/statements?method=PUT", null, ct));
        await AssertBadRequestAsync(await PostFormAsync(client, "PATCH", new() { ["statementId"] = id.ToString(), ["content"] = statement }));
        await AssertBadRequestAsync(await PostFormAsync(client, "PUT", new() { ["statementId"] = id.ToString(), ["Content"] = statement }));

        using var get = await app.Client().GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Xapi_2_0_requests_cannot_use_the_alternate_syntax()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var id = Guid.NewGuid();
        var fields = new Dictionary<string, string> { ["statementId"] = id.ToString(), ["content"] = Statement().ToJsonString() };

        using (var response = await PostFormAsync(app.Client(version: "2.0.0"), "PUT", fields))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("2.0", (await ReadObjectAsync(response))["detail"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        // The form field wins over the 1.0.3 header, so this is a 2.0 request too
        using (var response = await PostFormAsync(app.Client(version: "1.0.3"), "PUT", new(fields) { ["X-Experience-API-Version"] = "2.0.0" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var get = await app.Client().GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task A_form_over_MaxAlternateRequestBytes_gets_413_before_it_is_authenticated()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var id = Guid.NewGuid();
        var statement = Statement();

        // About 1.1 MiB of form data against the default limit of 1 MiB; the statement alone fits MaxStatementBytes
        statement["result"] = new System.Text.Json.Nodes.JsonObject
        {
            ["extensions"] = new System.Text.Json.Nodes.JsonObject { ["http://example.com/extensions/padding"] = new string('x', 1100 * 1024) },
        };
        var fields = new Dictionary<string, string>
        {
            ["statementId"] = id.ToString(),
            ["content"] = statement.ToJsonString(),
            ["Authorization"] = Authorization,
            ["X-Experience-API-Version"] = "1.0.3",
        };

        using (var response = await PostFormAsync(app.Anonymous(version: null), "PUT", fields))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal("1.0.3", Header(response, "X-Experience-API-Version"));
        }

        // The size is checked before the credentials in the form are
        using (var response = await PostFormAsync(app.Anonymous(version: null), "PUT", new(fields) { ["Authorization"] = Basic(Credentials.All, "wrong").ToString() }))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }

        // Without Content-Length, the limit is checked while reading
        var request = new HttpRequestMessage(HttpMethod.Post, "/xapi/statements?method=PUT")
        {
            Content = new StreamContent(new MemoryStream(await Form(fields).ReadAsByteArrayAsync(TestContext.Current.CancellationToken))),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
        request.Headers.TransferEncodingChunked = true;
        using (var chunked = await app.Anonymous(version: null).SendAsync(request, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        }

        using var get = await app.Client().GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public void MaxAlternateRequestBytes_must_be_positive()
    {
        var options = new XapiLrsOptions();

        Assert.Equal(1024 * 1024, options.MaxAlternateRequestBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxAlternateRequestBytes = 0);
    }

    [Fact]
    public async Task Without_1_0_3_support_the_alternate_syntax_is_rejected()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddXapiLrs(o => o.EnableVersion1_0_3 = false));

        using var response = await PostFormAsync(app.Client(version: "2.0.0"), "GET", new() { ["Authorization"] = Authorization, ["X-Experience-API-Version"] = "1.0.3" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string method, Dictionary<string, string> fields) =>
        client.PostAsync($"/xapi/statements?method={method}", Form(fields), TestContext.Current.CancellationToken);

    private static FormUrlEncodedContent Form(Dictionary<string, string> fields) => new(fields);

    private static async Task AssertBadRequestAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
