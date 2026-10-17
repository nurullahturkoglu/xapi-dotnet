using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.DocumentApi;
using static Xapi.Lrs.Tests.Infrastructure.MultipartApi;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// MaxStatementBytes, MaxAttachmentBytes and MaxDocumentBytes against the server's own request body limit.
public class RequestLimitTests(LrsHosts hosts)
{
    private const int KiB = 1024;

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Write_requests_follow_the_lrs_limits_rather_than_the_server_default(TestDatabase database)
    {
        // A server-wide limit of 64 KiB stands in for Kestrel's default of about 28.6 MiB
        var app = await hosts.CreateAsync(
            database,
            services => services.Configure<XapiLrsOptions>(o =>
            {
                o.MaxStatementBytes = 128 * KiB;
                o.MaxAttachmentBytes = 256 * KiB;
                o.MaxDocumentBytes = 256 * KiB;
            }),
            kestrel: k => k.Limits.MaxRequestBodySize = 64 * KiB);
        var client = app.Client();

        using (var json = await client.PostStatementsAsync(Padded(100 * KiB)))
        {
            Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        }

        var part = new Part(BinaryData(200 * KiB));
        using (var multipart = await client.PostMultipartAsync(Padded(100 * KiB).WithAttachments(Attachment(part)), part))
        {
            Assert.Equal(HttpStatusCode.OK, multipart.StatusCode);
        }

        var target = DocTarget.New(DocumentResource.State);
        using (var document = await client.PutDocAsync(target, new string('x', 200 * KiB), "text/plain"))
        {
            Assert.Equal(HttpStatusCode.NoContent, document.StatusCode);
        }

        using var over = await client.PostStatementsAsync(Padded(130 * KiB));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);
    }

    [Fact]
    public async Task Statement_json_over_the_limit_is_rejected_with_413()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.Configure<XapiLrsOptions>(o => o.MaxStatementBytes = 4 * KiB));
        var client = app.Client();

        using (var fits = await client.PostStatementsAsync(Padded(3 * KiB)))
        {
            Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        }

        using (var tooLarge = await client.PutStatementAsync(Guid.NewGuid(), Padded(5 * KiB)))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
            Assert.Equal("2.0.0", Header(tooLarge, "X-Experience-API-Version"));
        }

        // Without Content-Length the limit is checked while reading
        var request = new HttpRequestMessage(HttpMethod.Post, "/xapi/statements")
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Padded(5 * KiB).ToJsonString()))),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;
        using (var chunked = await client.SendAsync(request, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
        }

        // In multipart/mixed, the limit applies to the statement part
        var part = new Part(BinaryData(100));
        using var multipart = await client.PostMultipartAsync(Padded(5 * KiB).WithAttachments(Attachment(part)), part);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, multipart.StatusCode);
    }

    [Fact]
    public void MaxStatementBytes_must_be_positive()
    {
        var options = new XapiLrsOptions();

        Assert.Equal(16 * KiB * KiB, options.MaxStatementBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxStatementBytes = 0);
    }

    // A statement whose JSON is about the given size
    private static JsonObject Padded(int bytes)
    {
        var statement = Statement();
        statement["result"] = new JsonObject
        {
            ["extensions"] = new JsonObject { ["http://example.com/extensions/padding"] = new string('x', bytes - 250) },
        };
        return statement;
    }
}
