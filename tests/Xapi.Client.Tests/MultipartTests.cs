using System.Net;
using System.Text;
using Xapi.Client.Internal;
using Xapi.Client.Tests.Fakes;
using Xapi.Client.Tests.TestData;
using Xapi.Json;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class MultipartTests
{
    // Every byte value, plus sequences that look like line breaks and delimiters
    private static readonly byte[] BinaryContent =
    [
        .. Enumerable.Range(0, 256).Select(i => (byte)i),
        .. "\r\n--xapi-\r\n\r\n--"u8.ToArray(),
        0, 0xFF,
    ];

    private static readonly LanguageMap Display = LanguageMap.Create("en", "Certificate");
    private static readonly Iri UsageType = new("https://example.com/attachment-usage/certificate");

    [Fact]
    public void Create_computes_the_sha2_of_the_content()
    {
        // The example of the xAPI 1.0.3 specification, "Multipart/Mixed"
        var payload = AttachmentPayload.Create(Encoding.ASCII.GetBytes("here is a simple attachment"), "text/plain");

        Assert.Equal("495395e777cd98da653df9615d09c0fd6bb2f8d4788394cd53c56a3bfdcd848a", payload.Sha2);
    }

    [Fact]
    public void To_attachment_copies_content_type_length_and_sha2()
    {
        var payload = AttachmentPayload.Create(BinaryContent, "application/pdf");

        var attachment = payload.ToAttachment(UsageType, Display);

        Assert.Equal(new Attachment(UsageType, Display, "application/pdf", BinaryContent.Length, payload.Sha2), attachment);
    }

    [Fact]
    public async Task Statement_with_attachment_data_is_sent_as_multipart_with_hash_and_binary_parts()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(1)}\"]");
        var payload = AttachmentPayload.Create(BinaryContent, "application/octet-stream");
        var statement = Samples.Statement() with { Attachments = [payload.ToAttachment(UsageType, Display)] };

        await client.SaveStatementAsync(statement, [payload], cancellationToken: Ct);

        var request = handler.Single;
        Assert.StartsWith("multipart/mixed; boundary=", request.ContentType, StringComparison.Ordinal);
        var boundary = request.ContentType!.Substring("multipart/mixed; boundary=".Length).Trim('"');
        var parts = MultipartMixed.Read(request.Body!, boundary);

        Assert.Equal(2, parts.Count);
        Assert.Equal("application/json", parts[0].Header("Content-Type"));
        Assert.Equal(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), Encoding.UTF8.GetString(parts[0].Body.ToArray()));
        Assert.Equal("application/octet-stream", parts[1].Header("Content-Type"));
        Assert.Equal("binary", parts[1].Header("Content-Transfer-Encoding"));
        Assert.Equal(payload.Sha2, parts[1].Header("X-Experience-API-Hash"));
        Assert.Equal(BinaryContent, parts[1].Body.ToArray());
    }

    [Fact]
    public async Task Multipart_request_uses_crlf_framing()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(1)}\"]");
        var payload = AttachmentPayload.Create("abc"u8.ToArray(), "text/plain");
        var statement = Samples.Statement() with { Attachments = [payload.ToAttachment(UsageType, Display)] };

        await client.SaveStatementAsync(statement, [payload], cancellationToken: Ct);

        var request = handler.Single;
        var boundary = request.ContentType!.Substring("multipart/mixed; boundary=".Length);
        var json = XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0);
        Assert.Equal(
            $"--{boundary}\r\nContent-Type: application/json\r\n\r\n{json}\r\n"
            + $"--{boundary}\r\nContent-Type: text/plain\r\nContent-Transfer-Encoding: binary\r\nX-Experience-API-Hash: {payload.Sha2}\r\n\r\nabc\r\n"
            + $"--{boundary}--\r\n",
            request.BodyText);
    }

    [Fact]
    public async Task Data_shared_by_two_statements_is_sent_once()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(1)}\",\"{Samples.Id(2)}\"]");
        var payload = AttachmentPayload.Create(BinaryContent, "application/octet-stream");
        var statement = Samples.Statement() with { Attachments = [payload.ToAttachment(UsageType, Display)] };

        await client.SaveStatementsAsync([statement, statement], [payload, payload], cancellationToken: Ct);

        var boundary = handler.Single.ContentType!.Substring("multipart/mixed; boundary=".Length);
        Assert.Equal(2, MultipartMixed.Read(handler.Single.Body!, boundary).Count);
    }

    [Fact]
    public async Task Attachment_without_file_url_or_data_is_rejected_before_sending()
    {
        var (client, handler) = TestClient.Create();
        var payload = AttachmentPayload.Create(BinaryContent, "application/octet-stream");
        var statement = Samples.Statement() with { Attachments = [payload.ToAttachment(UsageType, Display)] };

        var exception = await Assert.ThrowsAsync<XapiValidationException>(() => client.SaveStatementAsync(statement, cancellationToken: Ct));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("attachment.payload.missing", error.Code);
        Assert.Equal("$.attachments[0]", error.Path);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Attachment_with_file_url_needs_no_data_and_is_sent_as_json()
    {
        var (client, handler) = TestClient.Create();
        handler.RespondJson($"[\"{Samples.Id(1)}\"]");
        var attachment = AttachmentPayload.Create(BinaryContent, "application/pdf").ToAttachment(UsageType, Display) with
        {
            FileUrl = new Iri("https://example.com/files/certificate.pdf"),
        };

        await client.SaveStatementAsync(Samples.Statement() with { Attachments = [attachment] }, cancellationToken: Ct);

        Assert.Equal("application/json", handler.Single.ContentType);
    }

    [Fact]
    public async Task Data_that_no_attachment_references_is_rejected_before_sending()
    {
        var (client, handler) = TestClient.Create();
        var payload = AttachmentPayload.Create(BinaryContent, "application/octet-stream");

        var exception = await Assert.ThrowsAsync<XapiValidationException>(() => client.SaveStatementAsync(Samples.Statement(), [payload], cancellationToken: Ct));

        Assert.Equal("attachment.payload.unreferenced", Assert.Single(exception.Errors).Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Sub_statement_attachments_are_matched_too()
    {
        var (client, handler) = TestClient.Create();
        var payload = AttachmentPayload.Create(BinaryContent, "application/octet-stream");
        var sub = new SubStatement(Samples.Learner, Verbs.Experienced, new Activity(Samples.ActivityId))
        {
            Attachments = [payload.ToAttachment(UsageType, Display)],
        };

        var exception = await Assert.ThrowsAsync<XapiValidationException>(
            () => client.SaveStatementsAsync([new Statement(Samples.Learner, Verbs.Attempted, sub)], cancellationToken: Ct));

        Assert.Equal("$[0].object.attachments[0]", Assert.Single(exception.Errors).Path);
    }

    [Fact]
    public async Task Multipart_query_response_returns_statements_and_attachment_bytes()
    {
        var (client, handler) = TestClient.Create();
        var payload = AttachmentPayload.Create(BinaryContent, "image/png");
        var statement = Samples.Statement(Samples.Id(1)) with { Attachments = [payload.ToAttachment(UsageType, Display)] };
        var json = $"{{\"statements\":[{XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0)}]}}";
        var body = MultipartMixed.Write("resp-boundary", Encoding.UTF8.GetBytes(json), [payload]);
        handler.Respond(_ => FakeHandler.Response(HttpStatusCode.OK, body, "multipart/mixed; boundary=\"resp-boundary\""));

        var result = await client.QueryStatementsAsync(new StatementQuery { Attachments = true }, cancellationToken: Ct);

        Assert.Equal(statement, Assert.Single(result.Statements));
        var received = Assert.Single(result.Attachments);
        Assert.Equal(payload.Sha2, received.Sha2);
        Assert.Equal("image/png", received.ContentType);
        Assert.Equal(BinaryContent, received.Content.ToArray());
    }

    [Fact]
    public async Task Multipart_response_with_wrong_hash_is_rejected()
    {
        var (client, handler) = TestClient.Create();
        var payload = AttachmentPayload.Create(BinaryContent, "image/png") with { Sha2 = new string('0', 64) };
        var body = MultipartMixed.Write("b", "{\"statements\":[]}"u8, [payload]);
        handler.Respond(_ => FakeHandler.Response(HttpStatusCode.OK, body, "multipart/mixed; boundary=b"));

        var exception = await Assert.ThrowsAsync<XapiHttpException>(() => client.QueryStatementsAsync(new StatementQuery { Attachments = true }, cancellationToken: Ct));

        Assert.Contains("SHA-2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_example_of_the_specification()
    {
        // xAPI 1.0.3, "Multipart/Mixed" example, with a preamble and a boundary that needs quoting
        const string Boundary = "abcABC0123'()+_,-./:=?";
        var text =
            "preamble\r\n"
            + $"--{Boundary}\r\nContent-Type:application/json\r\n\r\n{{\"a\":1}}\r\n"
            + $"--{Boundary}\r\nContent-Type:text/plain\r\nContent-Transfer-Encoding:binary\r\n"
            + "X-Experience-API-Hash:495395e777cd98da653df9615d09c0fd6bb2f8d4788394cd53c56a3bfdcd848a\r\n\r\n"
            + "here is a simple attachment\r\n"
            + $"--{Boundary}--";

        var parts = MultipartMixed.Read(Encoding.ASCII.GetBytes(text), Boundary);

        Assert.Equal(2, parts.Count);
        Assert.Equal("{\"a\":1}", Encoding.ASCII.GetString(parts[0].Body.ToArray()));
        Assert.Equal("here is a simple attachment", Encoding.ASCII.GetString(parts[1].Body.ToArray()));
        Assert.Equal("495395e777cd98da653df9615d09c0fd6bb2f8d4788394cd53c56a3bfdcd848a", parts[1].Header("x-experience-api-hash"));
    }

    [Fact]
    public void Reads_bare_lf_line_breaks_and_folded_headers()
    {
        var text = "--b\nContent-Type: application/json\n\n{}\n--b\nContent-Type: text/plain;\n charset=utf-8\n\nx\n--b--\n";

        var parts = MultipartMixed.Read(Encoding.ASCII.GetBytes(text), "b");

        Assert.Equal(["{}", "x"], parts.Select(p => Encoding.ASCII.GetString(p.Body.ToArray())));
        Assert.Equal("text/plain; charset=utf-8", parts[1].Header("Content-Type"));
    }

    [Theory]
    [InlineData("no boundary here")]
    [InlineData("--b\r\nContent-Type: application/json\r\n\r\n{}")]
    [InlineData("--b\r\nContent-Type: application/json\r\n{}\r\n--b--")]
    public void Rejects_malformed_bodies(string text) =>
        Assert.Throws<FormatException>(() => MultipartMixed.Read(Encoding.ASCII.GetBytes(text), "b"));

    [Fact]
    public void Payload_equality_compares_bytes_and_ignores_hash_case()
    {
        var a = AttachmentPayload.Create(BinaryContent, "image/png");
        var b = new AttachmentPayload(a.Sha2.ToUpperInvariant(), "image/png", BinaryContent.ToArray());

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Content = new byte[] { 1 } });
    }
}
