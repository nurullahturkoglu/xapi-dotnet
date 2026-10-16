using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Xapi.Client.Internal;
using Xapi.Json;

namespace Xapi.Client;

/// <summary>
/// Signs Statements as IEEE 9274.1.1, 4.2.6 "Statement Signing" describes: a JSON Web Signature (RFC 7515,
/// Compact Serialization, RS256, RS384 or RS512) of the Statement, sent as an Attachment with usage type
/// <see cref="SignatureUsageType"/> and content type <c>application/octet-stream</c>.
/// </summary>
/// <remarks>
/// An LRS checks that the signature is well formed and, when the JWS header carries the signer's certificate
/// (<c>x5c</c>), that it verifies with that certificate. Whether the signer is trusted is for whoever reads the
/// Statement to decide.
/// </remarks>
public static class StatementSigner
{
    /// <summary>The usage type of a signature Attachment, <c>http://adlnet.gov/expapi/attachments/signature</c>.</summary>
    public static Iri SignatureUsageType { get; } = new("http://adlnet.gov/expapi/attachments/signature");

    private const string SignatureContentType = "application/octet-stream";

    /// <summary>Signs a Statement with an RSA private key.</summary>
    /// <param name="statement">The complete Statement to sign. It must not have a signature Attachment yet.</param>
    /// <param name="privateKey">The RSA private key, for example from <see cref="X509Certificate2"/>'s <c>GetRSAPrivateKey()</c>.</param>
    /// <param name="version">The xAPI version the Statement is serialized and sent in.</param>
    /// <param name="hashAlgorithm">
    /// SHA-256, SHA-384 or SHA-512, for the JWS algorithm RS256, RS384 or RS512. The default value means SHA-256.
    /// </param>
    /// <param name="certificateChain">
    /// The certificate of <paramref name="privateKey"/> followed by the rest of its chain, put in the JWS
    /// <c>x5c</c> header (4.2.6: should be included when an X.509 certificate is used to sign). Optional.
    /// </param>
    /// <returns>The Statement with its signature Attachment object, and the signature to send with it.</returns>
    /// <exception cref="ArgumentException">The Statement already has a signature Attachment, the hash algorithm is not one of the three, or the chain contains a null entry.</exception>
    public static SignedStatement Sign(
        Statement statement,
        RSA privateKey,
        XapiVersion version,
        HashAlgorithmName hashAlgorithm = default,
        IReadOnlyList<X509Certificate2>? certificateChain = null)
    {
        Guard.NotNull(statement);
        Guard.NotNull(privateKey);
        if (statement.Attachments.Any(a => a.UsageType == SignatureUsageType))
        {
            throw new ArgumentException("The Statement already has a signature Attachment.", nameof(statement));
        }

        var hash = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
        var algorithm = hash == HashAlgorithmName.SHA256 ? "RS256"
            : hash == HashAlgorithmName.SHA384 ? "RS384"
            : hash == HashAlgorithmName.SHA512 ? "RS512"
            : throw new ArgumentException($"The hash algorithm must be SHA-256, SHA-384 or SHA-512, not {hash.Name}.", nameof(hashAlgorithm));
        if (certificateChain is not null && certificateChain.Any(c => c is null))
        {
            throw new ArgumentException("The certificate chain must not contain null.", nameof(certificateChain));
        }

        // 4.2.6: the payload is the complete Statement before the signature was added
        var payload = Encoding.UTF8.GetBytes(XapiJson.SerializeStatement(statement, version));
        var signingInput = Base64Url(Header(algorithm, certificateChain)) + "." + Base64Url(payload);
        var signature = privateKey.SignData(Encoding.ASCII.GetBytes(signingInput), hash, RSASignaturePadding.Pkcs1);
        var jws = AttachmentPayload.Create(Encoding.ASCII.GetBytes(signingInput + "." + Base64Url(signature)), SignatureContentType);

        var attachment = jws.ToAttachment(SignatureUsageType, LanguageMap.Create("en-US", "Signature"));
        var signed = statement with { Attachments = XapiList.CreateRange(statement.Attachments.Append(attachment)) };
        return new SignedStatement(signed, jws);
    }

    private static byte[] Header(string algorithm, IReadOnlyList<X509Certificate2>? certificateChain)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("alg", algorithm);
            if (certificateChain is { Count: > 0 })
            {
                // RFC 7515, 4.1.6: base64 (not base64url) DER, the signer's certificate first
                writer.WriteStartArray("x5c");
                foreach (var certificate in certificateChain)
                {
                    writer.WriteStringValue(Convert.ToBase64String(certificate.RawData));
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string Base64Url(byte[] data) =>
#if NET
        System.Buffers.Text.Base64Url.EncodeToString(data);
#else
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
#endif
}
