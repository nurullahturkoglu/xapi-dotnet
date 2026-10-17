using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Xapi.Lrs.Statements;

namespace Xapi.Lrs.Http;

// The body of a statement PUT or POST: the Statements JSON and, for multipart/mixed, the attachment parts keyed
// by their lower-case SHA-2.
internal sealed record StatementBody(byte[] Json, IReadOnlyDictionary<string, AttachmentPart> Parts);

// Reads application/json and multipart/mixed statement requests (IEEE 9274.1.1, 4.1.3). Attachment parts are
// copied as bytes; nothing here decodes them as text.
internal static class StatementBodyReader
{
    private const string HashHeader = "X-Experience-API-Hash";
    private const string TransferEncodingHeader = "Content-Transfer-Encoding";

    // Boundaries and part headers of a multipart/mixed request, on top of the statements and the attachment
    // data; a few hundred bytes per part in practice
    private const long MultipartOverheadBytes = 1024 * 1024;

    private static readonly Dictionary<string, AttachmentPart> NoParts = [];

    public static async Task<(StatementBody? Body, IResult? Error)> ReadAsync(HttpRequest request, XapiLrsOptions options, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
        {
            return (null, BadRequest("Statements must be sent as application/json or multipart/mixed."));
        }

        var maxStatementBytes = options.MaxStatementBytes;
        if (contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            RequestReading.SetBodyLimit(request.HttpContext, maxStatementBytes);
            var json = request.ContentLength > maxStatementBytes
                ? null
                : await ReadAllAsync(request.Body, maxStatementBytes, cancellationToken).ConfigureAwait(false);
            return json is null
                ? (null, StatementsTooLarge(maxStatementBytes))
                : (new StatementBody(json, NoParts), null);
        }

        if (!contentType.MediaType.Equals("multipart/mixed", StringComparison.OrdinalIgnoreCase))
        {
            return (null, BadRequest("Statements must be sent as application/json or multipart/mixed."));
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return (null, BadRequest("The multipart/mixed Content-Type has no boundary parameter."));
        }

        RequestReading.SetBodyLimit(request.HttpContext, Add(Add(maxStatementBytes, options.MaxAttachmentBytes), MultipartOverheadBytes));
        try
        {
            return await ReadMultipartAsync(new MultipartReader(boundary, request.Body), maxStatementBytes, options.MaxAttachmentBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException and not BadHttpRequestException)
        {
            // MultipartReader reports a missing closing delimiter or a malformed header block this way
            return (null, BadRequest("The multipart/mixed body is malformed: " + exception.Message));
        }
    }

    private static async Task<(StatementBody? Body, IResult? Error)> ReadMultipartAsync(MultipartReader reader, long maxStatementBytes, long maxAttachmentBytes, CancellationToken cancellationToken)
    {
        var first = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false);
        if (first is null)
        {
            return (null, BadRequest("The multipart/mixed body has no parts."));
        }

        // 4.1.3: the first part holds the Statements and has type application/json
        if (!MediaTypeHeaderValue.TryParse(first.ContentType, out var firstType) ||
            !firstType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return (null, BadRequest("The first part of a multipart/mixed statement request must have Content-Type application/json."));
        }

        var json = await ReadAllAsync(first.Body, maxStatementBytes, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return (null, StatementsTooLarge(maxStatementBytes));
        }

        var parts = new Dictionary<string, AttachmentPart>(StringComparer.Ordinal);
        var remaining = maxAttachmentBytes;
        for (var section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false);
             section is not null;
             section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false))
        {
            // 4.1.3: every part after the first carries X-Experience-API-Hash and Content-Transfer-Encoding:
            // binary; the LRS should assume binary when the header is missing
            var headers = section.Headers ?? [];
            if (!headers.TryGetValue(HashHeader, out var hashValues) || hashValues.Count != 1 || string.IsNullOrWhiteSpace(hashValues[0]))
            {
                return (null, BadRequest($"Every part after the first must have one {HashHeader} header."));
            }

            if (headers.TryGetValue(TransferEncodingHeader, out var encoding) &&
                !(encoding.Count == 1 && string.Equals(encoding[0]?.Trim(), "binary", StringComparison.OrdinalIgnoreCase)))
            {
                return (null, BadRequest($"Attachment parts must use {TransferEncodingHeader}: binary, not '{encoding}'."));
            }

            var declared = hashValues[0]!.Trim();
            if (!AttachmentHash.IsSupported(declared))
            {
                return (null, BadRequest($"The {HashHeader} '{declared}' is not a SHA-256, SHA-384 or SHA-512 hash in hexadecimal."));
            }

            var data = await ReadAllAsync(section.Body, remaining, cancellationToken).ConfigureAwait(false);
            if (data is null)
            {
                return (null, XapiResults.Problem(StatusCodes.Status413PayloadTooLarge, $"The attachments in this request are larger than the {maxAttachmentBytes} bytes this LRS accepts."));
            }

            remaining -= data.Length;

            // 4.1.3: data is matched to its Attachment object by comparing the SHA-2 of the raw data, and no other way
            var sha2 = AttachmentHash.Normalize(declared);
            if (!string.Equals(AttachmentHash.Compute(declared, data), sha2, StringComparison.Ordinal))
            {
                return (null, BadRequest($"The data of the part with {HashHeader} {declared} does not have that hash."));
            }

            // A second copy of the same data is redundant (4.1.3 "should only include one copy"), not an error
            parts.TryAdd(sha2, new AttachmentPart(sha2, string.IsNullOrWhiteSpace(section.ContentType) ? null : section.ContentType, data));
        }

        return (new StatementBody(json, parts), null);
    }

    // Reads a part, or returns null once it is longer than limit
    private static async Task<byte[]?> ReadAllAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static IResult StatementsTooLarge(long max) =>
        XapiResults.Problem(StatusCodes.Status413PayloadTooLarge, $"The statements in this request are larger than the {max} bytes this LRS accepts.");

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private static IResult BadRequest(string detail) => XapiResults.Problem(StatusCodes.Status400BadRequest, detail);
}
