using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.MultipartApi;
using static Xapi.Lrs.Tests.Infrastructure.QueryApi;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// multipart/mixed statement requests and the attachments=true response format (IEEE 9274.1.1, 4.1.3, 4.1.6.1).
public partial class AttachmentTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Binary_attachment_data_comes_back_byte_for_byte(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var id = Guid.NewGuid();
        var part = new Part(BinaryData(), "image/png");

        using (var put = await client.PutMultipartAsync(id, Statement().WithAttachments(Attachment(part)), part))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var response = await client.GetAsync($"/xapi/statements?statementId={id}&attachments=true", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^multipart/mixed; boundary=[^\";]+$", response.Content.Headers.ContentType!.ToString());
        var parts = await ReadPartsAsync(response);
        Assert.Equal(2, parts.Count);
        Assert.Equal("application/json", parts[0].Header("Content-Type"));
        Assert.Equal(id.ToString(), JsonOf(parts[0])["id"]!.GetValue<string>());
        Assert.Equal("image/png", parts[1].Header("Content-Type"));
        Assert.Equal("binary", parts[1].Header("Content-Transfer-Encoding"));
        Assert.Equal(part.Sha2, parts[1].Header("X-Experience-API-Hash"));
        Assert.Equal(part.Data, parts[1].Data);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Without_attachments_true_the_statement_comes_as_json(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var id = Guid.NewGuid();
        var part = new Part(BinaryData(64));

        using (var put = await client.PutMultipartAsync(id, Statement().WithAttachments(Attachment(part)), part))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        foreach (var query in new[] { $"statementId={id}", $"statementId={id}&attachments=false" })
        {
            using var response = await client.GetAsync("/xapi/statements?" + query, TestContext.Current.CancellationToken);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(part.Sha2, (await ReadObjectAsync(response))["attachments"]![0]!["sha2"]!.GetValue<string>());
        }
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task One_part_serves_every_statement_of_a_batch_that_uses_its_hash(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var verb = NewIri("verbs");
        var shared = new Part(Encoding.UTF8.GetBytes("shared " + Guid.NewGuid()), "text/plain");
        var own = new Part(BinaryData(300), "application/octet-stream");
        var statements = new JsonArray(
            Statement(verb: verb).WithAttachments(Attachment(shared)),
            Statement(verb: verb).WithAttachments(Attachment(shared), Attachment(own)));

        using (var post = await client.PostMultipartAsync(statements, shared, own))
        {
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        }

        using var response = await client.GetStatementsAsync(Query(("verb", verb), ("attachments", "true")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parts = await ReadPartsAsync(response);
        Assert.Equal(2, Ids(JsonOf(parts[0]).AsObject()).Count);
        Assert.Equal(3, parts.Count);
        Assert.Equal(shared.Data, parts.Single(p => p.Header("X-Experience-API-Hash") == shared.Sha2).Data);
        Assert.Equal(own.Data, parts.Single(p => p.Header("X-Experience-API-Hash") == own.Sha2).Data);

        await using var context = app.CreateDbContext();
        Assert.Equal(1, await context.AttachmentContents.CountAsync(c => c.Sha2 == shared.Sha2, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Data_sent_again_with_a_later_statement_is_stored_once(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var part = new Part(Encoding.UTF8.GetBytes("again " + Guid.NewGuid()), "text/plain");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        using (var put = await client.PutMultipartAsync(first, Statement().WithAttachments(Attachment(part)), part))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        // Upper-case hex in the statement still matches the part (and the stored row) by hash
        var upper = Attachment(part);
        upper["sha2"] = part.Sha2.ToUpperInvariant();
        using (var put = await client.PutMultipartAsync(second, Statement().WithAttachments(upper), part))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var response = await client.GetAsync($"/xapi/statements?statementId={second}&attachments=true", TestContext.Current.CancellationToken);
        Assert.Equal(part.Data, (await ReadPartsAsync(response))[1].Data);
        await using var context = app.CreateDbContext();
        Assert.Equal(1, await context.AttachmentContents.CountAsync(c => c.Sha2 == part.Sha2, TestContext.Current.CancellationToken));
        Assert.Equal(2, await context.StatementAttachments.CountAsync(c => c.Sha2 == part.Sha2, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task An_upper_case_part_hash_matches_and_is_kept_in_lower_case(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var data = Encoding.UTF8.GetBytes("upper " + Guid.NewGuid());
        var sha2 = Sha256(data);
        var part = new Part(data, "text/plain", Hash: sha2.ToUpperInvariant());
        var attachment = Attachment(part);
        attachment["sha2"] = sha2;
        var id = Guid.NewGuid();

        using (var put = await client.PutMultipartAsync(id, Statement().WithAttachments(attachment), part))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var response = await client.GetAsync($"/xapi/statements?statementId={id}&attachments=true", TestContext.Current.CancellationToken);
        var parts = await ReadPartsAsync(response);
        Assert.Equal(sha2, parts[1].Header("X-Experience-API-Hash"));
        Assert.Equal(data, parts[1].Data);

        await using var context = app.CreateDbContext();
        Assert.Equal(1, await context.AttachmentContents.CountAsync(c => c.Sha2 == sha2, TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.StatementAttachments.CountAsync(c => c.Sha2 == sha2, TestContext.Current.CancellationToken));

        // Lookups take either case
        var store = app.Services.GetRequiredService<IStatementStore>();
        Assert.Equal(sha2, (await store.GetAttachmentContentAsync(sha2.ToUpperInvariant(), TestContext.Current.CancellationToken))?.Sha2);
        Assert.Equal(sha2, new AttachmentSummary(sha2.ToUpperInvariant(), "text/plain", data.Length).Sha2);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Multipart_requests_without_attachment_data_are_accepted(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var withFileUrl = Statement().WithAttachments(Attachment(new Part([1, 2, 3]), fileUrl: "http://example.com/files/1"));

        using (var none = await client.PostMultipartAsync(Statement()))
        {
            Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        }

        using (var fileUrl = await client.PostMultipartAsync(withFileUrl))
        {
            Assert.Equal(HttpStatusCode.OK, fileUrl.StatusCode);
        }

        using var json = await client.PostStatementsAsync(Statement().WithAttachments(Attachment(new Part([4]), fileUrl: "http://example.com/files/2")));
        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
    }

    [Fact]
    public async Task Data_for_an_attachment_with_a_file_url_is_kept_too()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var id = Guid.NewGuid();
        var part = new Part(BinaryData(40));

        using (var put = await client.PutMultipartAsync(id, Statement().WithAttachments(Attachment(part, fileUrl: "http://example.com/f")), part))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var response = await client.GetAsync($"/xapi/statements?statementId={id}&attachments=true", TestContext.Current.CancellationToken);
        Assert.Equal(part.Data, (await ReadPartsAsync(response))[1].Data);
    }

    public static TheoryData<string> Malformed() =>
    [
        "missing part", "extra part", "wrong hash", "data that does not have its hash", "no hash header", "base64 encoding",
        "first part not json", "statements in two parts", "no boundary in body", "no closing delimiter",
        "sha-1 length hash", "json without file url",
    ];

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Malformed_multipart_requests_are_rejected_with_400(string kind)
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var part = new Part(Encoding.UTF8.GetBytes("attachment " + Guid.NewGuid()), "text/plain");
        var other = new Part(Encoding.UTF8.GetBytes("other " + Guid.NewGuid()), "text/plain");
        var statement = Statement().WithAttachments(Attachment(part)).ToJsonString();

        using var response = kind switch
        {
            "missing part" => await client.PostRawAsync(Body(statement, [])),
            "extra part" => await client.PostRawAsync(Body(statement, [part, other])),
            "wrong hash" => await client.PostRawAsync(Body(statement, [part with { Hash = other.Sha2 }])),
            "data that does not have its hash" => await client.PostRawAsync(Body(statement, [part with { Data = other.Data, Hash = part.Sha2 }])),
            "no hash header" => await client.PostRawAsync(Encoding.UTF8.GetBytes(
                $"--{Boundary}\r\nContent-Type: application/json\r\n\r\n{statement}\r\n--{Boundary}\r\nContent-Type: text/plain\r\n\r\nx\r\n--{Boundary}--\r\n")),
            "base64 encoding" => await client.PostRawAsync(Body(statement, [part with { TransferEncoding = "base64" }])),
            "first part not json" => await client.PostRawAsync(Body(statement, [part], firstContentType: "text/plain")),
            "statements in two parts" => await client.PostRawAsync(Encoding.UTF8.GetBytes(
                $"--{Boundary}\r\nContent-Type: application/json\r\n\r\n{statement}\r\n--{Boundary}\r\nContent-Type: application/json\r\n\r\n{statement}\r\n--{Boundary}--\r\n")),
            "no boundary in body" => await client.PostRawAsync(Encoding.UTF8.GetBytes(
                $"Content-Type: application/json\r\n\r\n{statement}\r\n--{Boundary}\r\nContent-Type: text/plain\r\nX-Experience-API-Hash: {part.Sha2}\r\n\r\n{Encoding.UTF8.GetString(part.Data)}\r\n--{Boundary}--\r\n")),
            "no closing delimiter" => await client.PostRawAsync(Body(statement, [part])[..^(Boundary.Length + 8)]),
            "sha-1 length hash" => await client.PostRawAsync(Body(statement, [part with { Hash = new string('a', 40) }])),
            "json without file url" => await client.PostStatementsAsync(Statement().WithAttachments(Attachment(part))),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_multipart_content_type_without_a_boundary_is_rejected_with_400()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        using var content = new ByteArrayContent(Body(Statement().ToJsonString(), []));
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/mixed;");

        using var response = await client.PostAsync("/xapi/statements", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_transfer_encoding_header_is_taken_as_binary()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var part = new Part(BinaryData(100), ContentType: null, TransferEncoding: null);

        using var response = await client.PostMultipartAsync(Statement().WithAttachments(Attachment(part, contentType: "image/gif")), part);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = Guid.Parse((await ReadArrayAsync(response))[0]!.GetValue<string>());
        using var get = await client.GetAsync($"/xapi/statements?statementId={id}&attachments=true", TestContext.Current.CancellationToken);
        var received = (await ReadPartsAsync(get))[1];
        Assert.Equal(part.Data, received.Data);

        // A part without a Content-Type is served with the one its Attachment object declares
        Assert.Equal("image/gif", received.Header("Content-Type"));
    }

    [Fact]
    public async Task Part_header_names_are_case_insensitive()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var part = new Part(BinaryData(50));
        var statement = Statement().WithAttachments(Attachment(part)).ToJsonString();
        var body = Encoding.UTF8.GetBytes(
            $"--{Boundary}\r\ncontent-type: application/json\r\n\r\n{statement}\r\n--{Boundary}\r\n" +
            $"CONTENT-TYPE: application/octet-stream\r\ncontent-transfer-encoding: BINARY\r\nx-experience-api-hash: {part.Sha2}\r\n\r\n");

        using var response = await client.PostRawAsync([.. body, .. part.Data, .. Encoding.ASCII.GetBytes($"\r\n--{Boundary}--\r\n")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Attachment_data_over_the_limit_is_rejected_with_413(TestDatabase database)
    {
        var app = await hosts.CreateAsync(database, services => services.Configure<XapiLrsOptions>(o => o.MaxAttachmentBytes = 1000));
        var client = app.Client();
        var small = new Part(BinaryData(600));
        var large = new Part(BinaryData(1001));
        var second = new Part(BinaryData(500));

        using (var fits = await client.PostMultipartAsync(Statement().WithAttachments(Attachment(small)), small))
        {
            Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        }

        using (var tooLarge = await client.PostMultipartAsync(Statement().WithAttachments(Attachment(large)), large))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        }

        // The limit counts all the parts of one request
        using var together = await client.PostMultipartAsync(Statement().WithAttachments(Attachment(small), Attachment(second)), small, second);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, together.StatusCode);
        Assert.Equal("2.0.0", Header(together, "X-Experience-API-Version"));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_rejected_batch_stores_no_attachment_data(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        var part = new Part(Encoding.UTF8.GetBytes("rejected " + Guid.NewGuid()));
        var id = Guid.NewGuid();
        using (var put = await client.PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        // Same id, different statement: 409, and the attachment of the batch is not kept
        var conflicting = Statement(id, verb: NewIri("verbs")).WithAttachments(Attachment(part));
        using var response = await client.PostMultipartAsync(new JsonArray(Statement().WithAttachments(Attachment(part)), conflicting), part);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var context = app.CreateDbContext();
        Assert.False(await context.AttachmentContents.AnyAsync(c => c.Sha2 == part.Sha2, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Attachment_data_is_read_one_attachment_at_a_time_and_not_at_all_for_HEAD(TestDatabase database)
    {
        var queries = new QueryLog();
        var app = await hosts.CreateAsync(database, services => Interception.Add(services, queries));
        var client = app.Client();
        var id = Guid.NewGuid();
        Part[] parts = [new(BinaryData(300), "image/png"), new(BinaryData(5000)), new(Encoding.UTF8.GetBytes("text " + Guid.NewGuid()), "text/plain")];
        using (var put = await client.PutMultipartAsync(id, Statement().WithAttachments([.. parts.Select(p => Attachment(p))]), parts))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var url = $"/xapi/statements?statementId={id}&attachments=true";
        queries.Queries.Clear();
        using var get = await client.GetAsync(url, TestContext.Current.CancellationToken);
        var body = await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, queries.Queries.Count(ReadsAttachmentData));
        Assert.Equal(body.Length, get.Content.Headers.ContentLength);
        var received = await ReadPartsAsync(get);
        Assert.Equal(id.ToString(), JsonOf(received[0])["id"]!.GetValue<string>());
        Assert.Equal(parts.Select(p => p.Sha2).Order(StringComparer.Ordinal), received.Skip(1).Select(p => p.Header("X-Experience-API-Hash")).Order(StringComparer.Ordinal));
        foreach (var part in parts)
        {
            var match = received.Single(r => r.Header("X-Experience-API-Hash") == part.Sha2);
            Assert.Equal(part.ContentType, match.Header("Content-Type"));
            Assert.Equal(part.Data, match.Data);
        }

        queries.Queries.Clear();
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(body.Length, head.Content.Headers.ContentLength);
        Assert.Equal(0, queries.Queries.Count(ReadsAttachmentData));
    }

    // A query that reads the content column of the attachment data table (not content_type)
    private static bool ReadsAttachmentData(string sql) =>
        sql.Contains("xapi_attachment_content", StringComparison.Ordinal) && ContentColumn().IsMatch(sql);

    [GeneratedRegex(@"\.""?content""?(?![_a-z])")]
    private static partial Regex ContentColumn();

    // The text of every query the LRS runs
    private sealed class QueryLog : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Queries { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
