using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xapi.Client;
using Xapi.Json;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.MultipartApi;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// Signed statements (IEEE 9274.1.1, 4.2.6): signatures made with StatementSigner are accepted, malformed or
// mismatching ones get 400.
public sealed class SignatureTests(LrsHosts hosts)
{
    // One key and a self-signed certificate for it, shared by the tests (key generation is slow)
    private static readonly RSA Key = RSA.Create(2048);
    private static readonly X509Certificate2 Certificate =
        new CertificateRequest("CN=xapi-dotnet test signer", Key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    public static IEnumerable<TheoryDataRow<TestDatabase, string>> Algorithms() =>
        from database in TestDatabases.All()
        from algorithm in new[] { "SHA256", "SHA384", "SHA512" }
        select new TheoryDataRow<TestDatabase, string>(database.Data, algorithm) { Skip = database.Skip };

    [Theory]
    [MemberData(nameof(Algorithms))]
    public async Task Signed_statements_are_accepted_and_returned_with_their_signature(TestDatabase database, string algorithm)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var signed = StatementSigner.Sign(NewStatement(), Key, XapiVersion.V2_0_0, new HashAlgorithmName(algorithm), [Certificate]);

        using var response = await client.PostRawAsync(Body(Serialize(signed.Statement), [Part(signed)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var get = await client.GetAsync($"/xapi/statements?statementId={signed.Statement.Id}&attachments=true", TestContext.Current.CancellationToken);
        var parts = await ReadPartsAsync(get);
        Assert.Equal(signed.Signature.Content.ToArray(), parts[1].Data);
        Assert.Equal("application/octet-stream", parts[1].Header("Content-Type"));
    }

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("2.0.0")]
    public async Task A_signature_without_a_certificate_is_checked_for_form_only(string version)
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client(version: version);
        var xapiVersion = version == "2.0.0" ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3;
        var signed = StatementSigner.Sign(NewStatement(), Key, xapiVersion);

        using var response = await client.PostRawAsync(Body(XapiJson.SerializeStatement(signed.Statement, xapiVersion), [Part(signed)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_statement_that_differs_from_its_signed_payload_is_rejected(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var signed = StatementSigner.Sign(NewStatement(), Key, XapiVersion.V2_0_0, certificateChain: [Certificate]);
        var tampered = signed.Statement with { Verb = new Verb(new Iri("http://example.com/verbs/changed")) };

        using var response = await client.PostRawAsync(Body(Serialize(tampered), [Part(signed)]));

        await AssertRejectedAsync(response, "does not match");
    }

    [Fact]
    public async Task A_signature_that_does_not_verify_with_its_certificate_is_rejected()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var statement = NewStatement();
        var jws = Encoding.ASCII.GetString(StatementSigner.Sign(statement, Key, XapiVersion.V2_0_0, certificateChain: [Certificate]).Signature.Content.Span);
        var segments = jws.Split('.');

        // Signed with another key than the certificate's
        using var otherKey = RSA.Create(2048);
        var wrongKey = StatementSigner.Sign(statement, otherKey, XapiVersion.V2_0_0, certificateChain: [Certificate]);
        using (var response = await client.PostRawAsync(Body(Serialize(wrongKey.Statement), [Part(wrongKey)])))
        {
            await AssertRejectedAsync(response, "does not verify");
        }

        // One bit of the signature changed
        var signature = Base64UrlDecode(segments[2]);
        signature[10] ^= 1;
        using var flipped = await SendJwsAsync(client, statement, $"{segments[0]}.{segments[1]}.{Base64Url(signature)}");
        await AssertRejectedAsync(flipped, "does not verify");
    }

    public static TheoryData<string> Malformed() =>
    [
        "HS256", "none", "ES256", "no alg", "crit", "x5c not an array", "x5c not a certificate", "two segments",
        "four segments", "not base64url", "header not json", "header not an object", "payload not json",
        "payload not a statement", "empty signature",
    ];

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task Malformed_signatures_are_rejected_with_400(string kind)
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var statement = NewStatement();
        var payload = Encoding.UTF8.GetBytes(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0));
        var certificate = Convert.ToBase64String(Certificate.RawData);
        var jws = kind switch
        {
            "HS256" => Hmac(payload),
            "none" => Jws("""{"alg":"none"}""", payload),
            "ES256" => Jws("""{"alg":"ES256"}""", payload),
            "no alg" => Jws("""{"typ":"JWT"}""", payload),
            "crit" => Jws("""{"alg":"RS256","crit":["exp"],"exp":1}""", payload),
            "x5c not an array" => Jws($$"""{"alg":"RS256","x5c":"{{certificate}}"}""", payload),
            "x5c not a certificate" => Jws("""{"alg":"RS256","x5c":["bm90IGEgY2VydGlmaWNhdGU="]}""", payload),
            "two segments" => string.Join('.', Jws("""{"alg":"RS256"}""", payload).Split('.')[..2]),
            "four segments" => Jws("""{"alg":"RS256"}""", payload) + ".AAAA",
            "not base64url" => Jws("""{"alg":"RS256"}""", payload).Replace('.', '+').Insert(10, "."),
            "header not json" => Jws("{alg:RS256}", payload),
            "header not an object" => Jws("""["RS256"]""", payload),
            "payload not json" => Jws("""{"alg":"RS256"}""", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload).Replace('"', '\''))),
            "payload not a statement" => Jws("""{"alg":"RS256"}""", """{"actor":{}}"""u8.ToArray()),
            "empty signature" => string.Join('.', Jws("""{"alg":"RS256"}""", payload).Split('.')[..2]) + ".",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        using var response = await SendJwsAsync(client, statement, jws);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_signature_attachment_must_be_application_octet_stream()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var statement = NewStatement();
        var jws = Encoding.ASCII.GetString(StatementSigner.Sign(statement, Key, XapiVersion.V2_0_0).Signature.Content.Span);

        using var response = await SendJwsAsync(client, statement, jws, contentType: "text/plain; charset=ascii");

        await AssertRejectedAsync(response, "application/octet-stream");
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Durations_are_compared_to_the_hundredth_of_a_second(TestDatabase database)
    {
        // IEEE 9274.1.1, 4.2.7: "any precision beyond 0.01 second precision shall not be included in the comparison"
        var client = (await hosts.SharedAsync(database)).Client();
        var signed = StatementSigner.Sign(
            NewStatement() with { Result = new Result { Duration = XapiDuration.Parse("P1DT12H36M0.12S") } },
            Key,
            XapiVersion.V2_0_0,
            certificateChain: [Certificate]);
        var json = Serialize(signed.Statement);

        using (var finer = await client.PostRawAsync(Body(json.Replace("0.12S", "0.1237S", StringComparison.Ordinal), [Part(signed)])))
        {
            Assert.Equal(HttpStatusCode.OK, finer.StatusCode);
        }

        var other = signed.Statement with { Id = Guid.NewGuid() };
        using var different = await client.PostRawAsync(Body(Serialize(other).Replace("0.12S", "0.13S", StringComparison.Ordinal), [Part(signed)]));
        await AssertRejectedAsync(different, "does not match");
    }

    [Fact]
    public async Task A_signature_attachment_with_only_a_file_url_is_not_checked()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var signed = StatementSigner.Sign(NewStatement(), Key, XapiVersion.V2_0_0);
        var attachment = signed.Statement.Attachments[0] with { FileUrl = new Iri("http://example.com/signatures/1") };

        using var response = await client.PostStatementsAsync(System.Text.Json.Nodes.JsonNode.Parse(Serialize(signed.Statement with { Attachments = XapiList.Create(attachment) }))!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Statement NewStatement() =>
        new(new Agent(new Mbox(NewMbox())) { Name = "Signer Test" }, new Verb(new Iri(NewIri("verbs"))), new Activity(new Iri(NewIri())))
        {
            Id = Guid.NewGuid(),
        };

    private static string Serialize(Statement statement) => XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0);

    private static Part Part(SignedStatement signed) => new(signed.Signature.Content.ToArray());

    // Sends the statement with the given JWS as its signature attachment
    private static Task<HttpResponseMessage> SendJwsAsync(HttpClient client, Statement statement, string jws, string contentType = "application/octet-stream")
    {
        var data = Encoding.ASCII.GetBytes(jws);
        var part = new Part(data, contentType);
        var attachment = new Attachment(StatementSigner.SignatureUsageType, LanguageMap.Create("en-US", "Signature"), contentType, data.Length, part.Sha2);
        return client.PostRawAsync(Body(Serialize(statement with { Attachments = XapiList.Create(attachment) }), [part]));
    }

    private static string Jws(string header, byte[] payload)
    {
        var input = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(payload);
        return input + "." + Base64Url(Key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static string Hmac(byte[] payload)
    {
        var input = Base64Url("""{"alg":"HS256"}"""u8.ToArray()) + "." + Base64Url(payload);
        return input + "." + Base64Url(HMACSHA256.HashData("secret"u8, Encoding.ASCII.GetBytes(input)));
    }

    private static string Base64Url(byte[] data) => System.Buffers.Text.Base64Url.EncodeToString(data);

    private static byte[] Base64UrlDecode(string text) => System.Buffers.Text.Base64Url.DecodeFromChars(text);

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string detail)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(detail, (await ReadObjectAsync(response))["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
