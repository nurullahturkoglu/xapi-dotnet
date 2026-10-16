using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Lrs.Statements;

// Checks that signed statements are well formed (IEEE 9274.1.1, 4.2.6 "Statement Signing"; xAPI 1.0.3 Data
// 2.6). As the standard notes, this catches mistakes in signatures; it does not authenticate the signer, so
// the x5c chain is not checked against trusted roots.
internal static class StatementSignatures
{
    public const string UsageType = "http://adlnet.gov/expapi/attachments/signature";

    /// <summary>Returns why the statement's signature is malformed, or null when it is fine or not signed.</summary>
    public static string? Validate(Statement statement, IReadOnlyDictionary<string, AttachmentPart> parts, XapiVersion version)
    {
        foreach (var attachment in statement.Attachments)
        {
            if (!string.Equals(attachment.UsageType.Value, UsageType, StringComparison.Ordinal))
            {
                continue;
            }

            // 4.2.6: the JWS is an Attachment with contentType application/octet-stream
            if (!MediaTypeHeaderValue.TryParse(attachment.ContentType, out var contentType) ||
                !contentType.MediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return $"A signature Attachment must have contentType application/octet-stream, not '{attachment.ContentType}'.";
            }

            // An Attachment with only a fileUrl was not sent, so there is nothing to decode
            if (parts.TryGetValue(AttachmentHash.Normalize(attachment.Sha2), out var part) &&
                Check(part.Data, statement, version) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    private static string? Check(byte[] jws, Statement received, XapiVersion version)
    {
        // JWS Compact Serialization (RFC 7515, 7.1): three base64url segments separated by periods
        var span = jws.AsSpan();
        var firstDot = span.IndexOf((byte)'.');
        var secondDot = firstDot < 0 ? -1 : span[(firstDot + 1)..].IndexOf((byte)'.') + firstDot + 1;
        if (firstDot < 0 || secondDot <= firstDot || span[(secondDot + 1)..].Contains((byte)'.'))
        {
            return "The signature is not a JWS in Compact Serialization (header.payload.signature).";
        }

        if (!TryDecode(span[..firstDot], out var headerBytes) ||
            !TryDecode(span[(firstDot + 1)..secondDot], out var payload) ||
            !TryDecode(span[(secondDot + 1)..], out var signature))
        {
            return "A segment of the JWS signature is not base64url.";
        }

        if (signature.Length == 0)
        {
            return "The JWS has an empty signature.";
        }

        var header = ReadHeader(headerBytes, out var headerError);
        if (header is null)
        {
            return headerError;
        }

        using var certificate = header.Certificate;

        // 4.2.6: the payload is a JSON serialization of the complete Statement before the signature was added
        var signed = XapiJson.ParseStatement(payload, version);
        if (!signed.IsSuccess)
        {
            return "The JWS payload is not a valid Statement: " + string.Join("; ", signed.Errors.Select(e => e.Message));
        }

        // 4.2.6 "Validate that the original Statement is logically equivalent to the received Statement", by the
        // immutability comparison of 4.2 (durations to 0.01 second, 4.2.7)
        if (!StatementEquivalence.AreEquivalent(signed.Value, received))
        {
            return "The signed Statement in the JWS payload does not match the Statement it is attached to.";
        }

        // 4.2.6: "If the JWS header includes an X.509 certificate, validate the signature against that certificate"
        if (certificate is not null)
        {
            using var key = certificate.GetRSAPublicKey();
            if (key is null)
            {
                return "The certificate in the JWS x5c header does not have an RSA key.";
            }

            if (!key.VerifyData(span[..secondDot], signature, header.Hash, RSASignaturePadding.Pkcs1))
            {
                return "The JWS signature does not verify with the certificate in its x5c header.";
            }
        }

        return null;
    }

    private static JwsHeader? ReadHeader(byte[] json, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The JWS header is not a JSON object.";
                return null;
            }

            // 4.2.6: RS256, RS384 or RS512 only
            var hash = root.TryGetProperty("alg", out var alg) && alg.ValueKind == JsonValueKind.String
                ? alg.GetString() switch
                {
                    "RS256" => HashAlgorithmName.SHA256,
                    "RS384" => HashAlgorithmName.SHA384,
                    "RS512" => HashAlgorithmName.SHA512,
                    _ => default,
                }
                : default;
            if (hash == default)
            {
                error = $"The JWS signature must use the algorithm RS256, RS384 or RS512, not {(alg.ValueKind == JsonValueKind.String ? alg.GetString() : "none")}.";
                return null;
            }

            // RFC 7515, 4.1.11: extensions listed in crit must be understood, and this LRS understands none
            if (root.TryGetProperty("crit", out _))
            {
                error = "The JWS header lists critical extensions (crit), which this LRS does not support.";
                return null;
            }

            X509Certificate2? certificate = null;
            if (root.TryGetProperty("x5c", out var x5c))
            {
                // RFC 7515, 4.1.6: an array of base64 (not base64url) DER certificates, the signer's first
                if (x5c.ValueKind != JsonValueKind.Array || x5c.GetArrayLength() == 0 ||
                    x5c.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.String))
                {
                    error = "The JWS x5c header must be a non-empty array of base64 certificates.";
                    return null;
                }

                try
                {
                    certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(x5c[0].GetString()!));
                }
                catch (Exception exception) when (exception is FormatException or CryptographicException)
                {
                    error = "The first certificate in the JWS x5c header cannot be read: " + exception.Message;
                    return null;
                }
            }

            return new JwsHeader(hash, certificate);
        }
        catch (JsonException)
        {
            error = "The JWS header is not valid JSON.";
            return null;
        }
    }

    private static bool TryDecode(ReadOnlySpan<byte> segment, out byte[] decoded)
    {
        decoded = [];
        if (!Base64Url.IsValid(segment, out var length))
        {
            return false;
        }

        decoded = new byte[length];
        return Base64Url.DecodeFromUtf8(segment, decoded, out _, out var written) == System.Buffers.OperationStatus.Done && written == length;
    }

    private sealed record JwsHeader(HashAlgorithmName Hash, X509Certificate2? Certificate);
}
