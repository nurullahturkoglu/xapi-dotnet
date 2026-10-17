using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// The multipart/mixed response format of attachments=true (IEEE 9274.1.1, 4.1.3 and 4.1.6.1 GET): the
// Statement or StatementResult JSON first, then one part per attachment, written as raw bytes. The data is read
// from the store one attachment at a time while the body is written, so a response holds at most one
// attachment in memory however many the statements carry. The lengths are known from the store, so the
// response still has a Content-Length, and HEAD reads no attachment data at all.
internal sealed class MultipartResponse(
    ReadOnlyMemory<byte> json,
    IReadOnlyList<AttachmentSummary> attachments,
    IStatementStore store,
    DateTimeOffset? lastModified) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        var cancellationToken = httpContext.RequestAborted;

        string boundary;
        do
        {
            // Random, so attachment data, stored before the boundary existed, cannot be made to contain it
            boundary = "xapi-" + RandomNumberGenerator.GetHexString(32, lowercase: true);
        }
        while (json.Span.IndexOf(Encoding.ASCII.GetBytes("--" + boundary)) >= 0);

        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var first = Encoding.ASCII.GetBytes($"--{boundary}\r\nContent-Type: application/json\r\n\r\n");
        var headers = attachments
            .Select(a => Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\nContent-Type: {a.ContentType}\r\nContent-Transfer-Encoding: binary\r\nX-Experience-API-Hash: {a.Sha2}\r\n\r\n"))
            .ToList();
        var last = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");

        response.StatusCode = StatusCodes.Status200OK;
        if (lastModified is { } modified)
        {
            response.Headers[HeaderNames.LastModified] = HeaderUtilities.FormatDate(modified);
        }

        // No quotes and one space: some clients (the conformance suite among them) split the header by hand
        response.ContentType = $"multipart/mixed; boundary={boundary}";
        response.ContentLength = first.Length + json.Length + headers.Sum(h => (long)h.Length) + attachments.Sum(a => a.Length) + last.Length;
        if (HttpMethods.IsHead(httpContext.Request.Method))
        {
            return;
        }

        await response.Body.WriteAsync(first, cancellationToken).ConfigureAwait(false);
        await response.Body.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < attachments.Count; i++)
        {
            var summary = attachments[i];
            var content = await store.GetAttachmentContentAsync(summary.Sha2, cancellationToken).ConfigureAwait(false);

            // The headers are sent, so a mismatch can only abort the response, never be answered with an error
            if (content is null || content.Content.Length != summary.Length)
            {
                throw new InvalidOperationException($"The attachment data with SHA-2 {summary.Sha2} changed while the response was written.");
            }

            if (content.Content.Span.IndexOf(delimiter) >= 0)
            {
                throw new InvalidOperationException("Attachment data contains the multipart boundary.");
            }

            await response.Body.WriteAsync(headers[i], cancellationToken).ConfigureAwait(false);
            await response.Body.WriteAsync(content.Content, cancellationToken).ConfigureAwait(false);
        }

        await response.Body.WriteAsync(last, cancellationToken).ConfigureAwait(false);
    }
}
