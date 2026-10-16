using System.Text;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// The multipart/mixed response format of attachments=true (IEEE 9274.1.1, 4.1.3 and 4.1.6.1 GET): the
// Statement or StatementResult JSON first, then one part per attachment, written as raw bytes.
internal static class MultipartResponse
{
    public static (string ContentType, byte[] Body) Write(string json, IReadOnlyList<AttachmentContent> attachments)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        string boundary;
        do
        {
            boundary = "xapi-" + Guid.NewGuid().ToString("N");
        }
        while (!IsSafe(Encoding.ASCII.GetBytes("--" + boundary), jsonBytes, attachments));

        using var body = new MemoryStream();
        Ascii(body, $"--{boundary}\r\nContent-Type: application/json\r\n\r\n");
        body.Write(jsonBytes);
        foreach (var attachment in attachments)
        {
            Ascii(body, $"\r\n--{boundary}\r\nContent-Type: {attachment.ContentType}\r\nContent-Transfer-Encoding: binary\r\nX-Experience-API-Hash: {attachment.Sha2}\r\n\r\n");
            body.Write(attachment.Content.Span);
        }

        Ascii(body, $"\r\n--{boundary}--\r\n");

        // No quotes and one space: some clients (the conformance suite among them) split the header by hand
        return ($"multipart/mixed; boundary={boundary}", body.ToArray());
    }

    // A random boundary is all but certain not to occur in the content; checking costs one scan
    private static bool IsSafe(byte[] delimiter, byte[] json, IReadOnlyList<AttachmentContent> attachments) =>
        json.AsSpan().IndexOf(delimiter) < 0 && attachments.All(a => a.Content.Span.IndexOf(delimiter) < 0);

    private static void Ascii(MemoryStream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
}
