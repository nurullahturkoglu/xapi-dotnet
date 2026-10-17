using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Xapi.Lrs.Http;

internal static class XapiHeaders
{
    public const string Version = "X-Experience-API-Version";
    public const string ConsistentThrough = "X-Experience-API-Consistent-Through";
}

internal static class XapiResults
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Errors are RFC 9457 problem details with the xAPI validation errors (code, JSON path, message) attached.
    public static IResult Problem(int status, string detail, IReadOnlyList<XapiError>? errors = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "about:blank");
            writer.WriteString("title", ReasonPhrases.GetReasonPhrase(status));
            writer.WriteNumber("status", status);
            writer.WriteString("detail", detail);
            if (errors is { Count: > 0 })
            {
                writer.WriteStartArray("errors");
                foreach (var error in errors)
                {
                    writer.WriteStartObject();
                    writer.WriteString("code", error.Code);
                    writer.WriteString("path", error.Path);
                    writer.WriteString("message", error.Message);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return new BodyResult(status, "application/problem+json", buffer.WrittenMemory);
    }

    public static IResult Json(string json, DateTimeOffset? lastModified = null) =>
        new BodyResult(StatusCodes.Status200OK, "application/json", Encoding.UTF8.GetBytes(json), lastModified);

    public static IResult Json(ReadOnlyMemory<byte> utf8Json, DateTimeOffset? lastModified = null) =>
        new BodyResult(StatusCodes.Status200OK, "application/json", utf8Json, lastModified);

    public static IResult Body(string contentType, ReadOnlyMemory<byte> body, DateTimeOffset? lastModified = null) =>
        new BodyResult(StatusCodes.Status200OK, contentType, body, lastModified);

    public static IResult Status(int status) => new BodyResult(status, null, ReadOnlyMemory<byte>.Empty);

    // Writes Content-Length and leaves the body out for HEAD, which must return the same headers as GET
    // (IEEE 9274.1.1, 4.1: HEAD on every GET resource).
    private sealed class BodyResult(int status, string? contentType, ReadOnlyMemory<byte> body, DateTimeOffset? lastModified = null) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = status;
            if (lastModified is { } modified)
            {
                response.Headers[HeaderNames.LastModified] = HeaderUtilities.FormatDate(modified);
            }

            if (contentType is null)
            {
                return;
            }

            response.ContentType = contentType;
            response.ContentLength = body.Length;
            if (!HttpMethods.IsHead(httpContext.Request.Method))
            {
                await response.Body.WriteAsync(body, httpContext.RequestAborted).ConfigureAwait(false);
            }
        }
    }
}
