using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Xapi.Lrs.Tests.Infrastructure;

// multipart/mixed statement requests built byte by byte, so tests can also send malformed ones, and a reader for
// multipart responses that keeps every part as bytes.
internal static class MultipartApi
{
    public const string Boundary = "test-boundary-6f1c0c";

    public sealed record Part(byte[] Data, string? ContentType = "application/octet-stream", string? Hash = null, string? TransferEncoding = "binary")
    {
        public string Sha2 => Hash ?? Sha256(Data);
    }

    public sealed record ReceivedPart(IReadOnlyDictionary<string, string> Headers, byte[] Data)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // Every byte value, CR LF pairs and dashes: anything a text decoder or a careless parser would change
    public static byte[] BinaryData(int length = 1024)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 7 % 256);
        }

        "\r\n--\r\n\xff\xfe"u8.CopyTo(data.AsSpan(length / 2));
        return data;
    }

    public static JsonObject Attachment(Part part, string? contentType = null, string? fileUrl = null, string usageType = "http://example.com/attachment-usage/test")
    {
        var attachment = new JsonObject
        {
            ["usageType"] = usageType,
            ["display"] = new JsonObject { ["en-US"] = "A test attachment" },
            ["contentType"] = contentType ?? part.ContentType ?? "application/octet-stream",
            ["length"] = part.Data.Length,
            ["sha2"] = part.Sha2,
        };
        if (fileUrl is not null)
        {
            attachment["fileUrl"] = fileUrl;
        }

        return attachment;
    }

    public static JsonObject WithAttachments(this JsonObject statement, params JsonObject[] attachments)
    {
        statement["attachments"] = new JsonArray([.. attachments]);
        return statement;
    }

    public static byte[] Body(string firstPart, IEnumerable<Part> parts, string firstContentType = "application/json")
    {
        using var body = new MemoryStream();
        Write(body, $"--{Boundary}\r\nContent-Type: {firstContentType}\r\n\r\n{firstPart}");
        foreach (var part in parts)
        {
            Write(body, $"\r\n--{Boundary}\r\n");
            if (part.ContentType is not null)
            {
                Write(body, $"Content-Type: {part.ContentType}\r\n");
            }

            if (part.TransferEncoding is not null)
            {
                Write(body, $"Content-Transfer-Encoding: {part.TransferEncoding}\r\n");
            }

            Write(body, $"X-Experience-API-Hash: {part.Sha2}\r\n\r\n");
            body.Write(part.Data);
        }

        Write(body, $"\r\n--{Boundary}--\r\n");
        return body.ToArray();
    }

    public static ByteArrayContent Content(byte[] body, string boundary = Boundary)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/mixed; boundary={boundary}");
        return content;
    }

    public static Task<HttpResponseMessage> PostMultipartAsync(this HttpClient client, JsonNode statements, params Part[] parts) =>
        client.PostAsync("/xapi/statements", Content(Body(statements.ToJsonString(), parts)), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PutMultipartAsync(this HttpClient client, Guid id, JsonNode statement, params Part[] parts) =>
        client.PutAsync($"/xapi/statements?statementId={id}", Content(Body(statement.ToJsonString(), parts)), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostRawAsync(this HttpClient client, byte[] body, string boundary = Boundary) =>
        client.PostAsync("/xapi/statements", Content(body, boundary), TestContext.Current.CancellationToken);

    public static async Task<List<ReceivedPart>> ReadPartsAsync(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType;
        Assert.Equal("multipart/mixed", contentType?.MediaType);
        var boundary = HeaderUtilities.RemoveQuotes(contentType!.Parameters.Single(p => p.Name == "boundary").Value).Value!;
        var reader = new MultipartReader(boundary, await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var parts = new List<ReceivedPart>();
        while (await reader.ReadNextSectionAsync(TestContext.Current.CancellationToken) is { } section)
        {
            using var data = new MemoryStream();
            await section.Body.CopyToAsync(data, TestContext.Current.CancellationToken);
            var headers = section.Headers!.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            parts.Add(new ReceivedPart(headers, data.ToArray()));
        }

        return parts;
    }

    public static JsonNode JsonOf(ReceivedPart part) => JsonNode.Parse(part.Data)!;

    private static void Write(MemoryStream stream, string text) => stream.Write(Encoding.UTF8.GetBytes(text));
}
