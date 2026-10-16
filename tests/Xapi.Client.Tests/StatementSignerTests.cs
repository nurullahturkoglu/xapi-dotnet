using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Xapi.Json;

namespace Xapi.Client.Tests;

public class StatementSignerTests
{
    private static readonly RSA Key = RSA.Create(2048);

    private static readonly Statement Statement = new(
        new Agent(new Mbox("mailto:signer@example.com")),
        Verbs.Completed,
        new Activity(new Iri("http://example.com/activities/signed")))
    {
        Id = new Guid("4fe3a6ad-1d3e-4bb7-9f0a-41d7c1e0c1a2"),
    };

    [Theory]
    [InlineData("SHA256", "RS256")]
    [InlineData("SHA384", "RS384")]
    [InlineData("SHA512", "RS512")]
    public void Sign_produces_a_compact_jws_of_the_statement(string hash, string algorithm)
    {
        var signed = StatementSigner.Sign(Statement, Key, XapiVersion.V2_0_0, new HashAlgorithmName(hash));

        var jws = Encoding.ASCII.GetString(signed.Signature.Content.ToArray());
        var segments = jws.Split('.');
        Assert.Equal(3, segments.Length);
        using (var header = JsonDocument.Parse(Base64UrlDecode(segments[0])))
        {
            Assert.Equal(algorithm, header.RootElement.GetProperty("alg").GetString());
            Assert.False(header.RootElement.TryGetProperty("x5c", out _));
        }

        Assert.Equal(XapiJson.SerializeStatement(Statement, XapiVersion.V2_0_0), Encoding.UTF8.GetString(Base64UrlDecode(segments[1])));
        Assert.True(Key.VerifyData(Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]), Base64UrlDecode(segments[2]), new HashAlgorithmName(hash), RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Sign_adds_the_signature_attachment_to_the_statement()
    {
        var other = AttachmentPayload.Create(new byte[] { 1, 2, 3 }, "image/png").ToAttachment(new Iri("http://example.com/usage"), LanguageMap.Create("en", "Picture"));
        var statement = Statement with { Attachments = XapiList.Create(other) };

        var signed = StatementSigner.Sign(statement, Key, XapiVersion.V2_0_0);

        Assert.Equal(statement with { Attachments = XapiList<Attachment>.Empty }, signed.Statement with { Attachments = XapiList<Attachment>.Empty });
        Assert.Equal(2, signed.Statement.Attachments.Count);
        Assert.Equal(other, signed.Statement.Attachments[0]);
        var attachment = signed.Statement.Attachments[1];
        Assert.Equal(StatementSigner.SignatureUsageType, attachment.UsageType);
        Assert.Equal("application/octet-stream", attachment.ContentType);
        Assert.Equal(signed.Signature.Content.Length, attachment.Length);
        Assert.Equal(signed.Signature.Sha2, attachment.Sha2);
        Assert.Equal("application/octet-stream", signed.Signature.ContentType);

        // The payload is the statement before the signature was added, its other attachments included
        var payload = Encoding.ASCII.GetString(signed.Signature.Content.ToArray()).Split('.')[1];
        Assert.Equal(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), Encoding.UTF8.GetString(Base64UrlDecode(payload)));
    }

    [Fact]
    public void Sign_puts_the_certificate_chain_in_the_x5c_header()
    {
        using var certificate = new CertificateRequest("CN=signer", Key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var issuerKey = RSA.Create(2048);
        using var issuer = new CertificateRequest("CN=issuer", issuerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var signed = StatementSigner.Sign(Statement, Key, XapiVersion.V1_0_3, HashAlgorithmName.SHA256, [certificate, issuer]);

        var header = Encoding.ASCII.GetString(signed.Signature.Content.ToArray()).Split('.')[0];
        using var json = JsonDocument.Parse(Base64UrlDecode(header));
        var x5c = json.RootElement.GetProperty("x5c").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal([Convert.ToBase64String(certificate.RawData), Convert.ToBase64String(issuer.RawData)], x5c);
    }

    [Fact]
    public void Sign_rejects_what_it_cannot_sign()
    {
        var signed = StatementSigner.Sign(Statement, Key, XapiVersion.V2_0_0);

        Assert.Throws<ArgumentException>("statement", () => StatementSigner.Sign(signed.Statement, Key, XapiVersion.V2_0_0));
        Assert.Throws<ArgumentException>("hashAlgorithm", () => StatementSigner.Sign(Statement, Key, XapiVersion.V2_0_0, HashAlgorithmName.MD5));
        Assert.Throws<ArgumentException>("certificateChain", () => StatementSigner.Sign(Statement, Key, XapiVersion.V2_0_0, certificateChain: [null!]));
        Assert.Throws<ArgumentNullException>("statement", () => StatementSigner.Sign(null!, Key, XapiVersion.V2_0_0));
        Assert.Throws<ArgumentNullException>("privateKey", () => StatementSigner.Sign(Statement, null!, XapiVersion.V2_0_0));
    }

    private static byte[] Base64UrlDecode(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}
