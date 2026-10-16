using System.Text;

namespace Xapi.Client.Internal;

/// <summary>
/// Writes and reads the <c>multipart/mixed</c> format of RFC 2046 as xAPI uses it for attachments
/// (IEEE 9274.1.1, 4.1.3). Everything works on bytes: attachment data is binary and must not pass through a text
/// decoder.
/// </summary>
internal static class MultipartMixed
{
    public const string HashHeader = "X-Experience-API-Hash";

    private static readonly byte[] CrLf = [(byte)'\r', (byte)'\n'];

    public static string NewBoundary() => "xapi-" + Guid.NewGuid().ToString("N");

    /// <summary>The Statements part first, then one part per distinct attachment (by SHA-2).</summary>
    public static byte[] Write(string boundary, ReadOnlySpan<byte> statementsJson, IEnumerable<AttachmentPayload> attachments)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "--" + boundary + "\r\nContent-Type: application/json\r\n\r\n");
        Write(stream, statementsJson);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            if (!written.Add(attachment.Sha2))
            {
                continue;
            }

            if (attachment.ContentType.IndexOfAny(['\r', '\n']) >= 0 || attachment.Sha2.IndexOfAny(['\r', '\n']) >= 0)
            {
                throw new ArgumentException("An attachment's content type and SHA-2 must not contain line breaks.", nameof(attachments));
            }

            WriteAscii(stream, "\r\n--" + boundary + "\r\n");
            WriteAscii(stream, "Content-Type: " + attachment.ContentType + "\r\n");
            WriteAscii(stream, "Content-Transfer-Encoding: binary\r\n");
            WriteAscii(stream, HashHeader + ": " + attachment.Sha2 + "\r\n\r\n");
            Write(stream, attachment.Content.Span);
        }

        WriteAscii(stream, "\r\n--" + boundary + "--\r\n");
        return stream.ToArray();
    }

    /// <summary>True when <paramref name="boundary"/> cannot be confused with any content.</summary>
    public static bool IsSafeBoundary(string boundary, ReadOnlySpan<byte> statementsJson, IEnumerable<AttachmentPayload> attachments)
    {
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        return statementsJson.IndexOf(delimiter) < 0 && attachments.All(a => a.Content.Span.IndexOf(delimiter) < 0);
    }

    /// <summary>Splits a multipart body into its parts. Line breaks may be CRLF or, leniently, a bare LF.</summary>
    /// <exception cref="FormatException">The body is not a well-formed multipart body for this boundary.</exception>
    public static List<MultipartPart> Read(byte[] body, string boundary)
    {
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var span = body.AsSpan();

        // The first delimiter may follow a preamble, but must start a line
        var position = span.StartsWith(delimiter) ? 0 : FindDelimiterLine(span, delimiter, 0).DelimiterStart;
        if (position < 0)
        {
            throw new FormatException("The multipart body does not contain the boundary.");
        }

        var parts = new List<MultipartPart>();
        position += delimiter.Length;
        while (true)
        {
            if (span.Slice(position).StartsWith("--"u8))
            {
                return parts;
            }

            // Transport padding, then the line break that ends the delimiter line
            while (position < span.Length && (span[position] == (byte)' ' || span[position] == (byte)'\t'))
            {
                position++;
            }

            position = SkipLineBreak(span, position);
            if (position < 0)
            {
                throw new FormatException("A multipart delimiter is not followed by a line break.");
            }

            var next = FindDelimiterLine(span, delimiter, position);
            if (next.DelimiterStart < 0)
            {
                throw new FormatException("The multipart body ends without a closing delimiter.");
            }

            parts.Add(ReadPart(body, position, next.LineBreakStart));
            position = next.DelimiterStart + delimiter.Length;
        }
    }

    private static MultipartPart ReadPart(byte[] body, int start, int end)
    {
        var span = body.AsSpan(start, end - start);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // An empty header block means the part starts with a line break
        int bodyStart;
        var afterEmptyHeaders = SkipLineBreak(span, 0);
        if (afterEmptyHeaders >= 0)
        {
            bodyStart = afterEmptyHeaders;
        }
        else
        {
            var crlf = span.IndexOf("\r\n\r\n"u8);
            var lf = span.IndexOf("\n\n"u8);
            int headerEnd;
            if (crlf >= 0 && (lf < 0 || crlf < lf))
            {
                headerEnd = crlf;
                bodyStart = crlf + 4;
            }
            else if (lf >= 0)
            {
                headerEnd = lf;
                bodyStart = lf + 2;
            }
            else
            {
                throw new FormatException("A multipart part has no blank line after its headers.");
            }

            ReadHeaders(Latin1(span.Slice(0, headerEnd)), headers);
        }

        return new MultipartPart(headers, new ReadOnlyMemory<byte>(body, start + bodyStart, span.Length - bodyStart));
    }

    private static void ReadHeaders(string text, Dictionary<string, string> headers)
    {
        string? last = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            // RFC 5322 folding: a line starting with white space continues the previous header
            if ((line[0] == ' ' || line[0] == '\t') && last is not null)
            {
                headers[last] += " " + line.Trim();
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new FormatException($"Invalid multipart header line: \"{line}\".");
            }

            last = line.Substring(0, colon).Trim();
            headers[last] = line.Substring(colon + 1).Trim();
        }
    }

    // Finds "<line break>--boundary" at or after "from"
    private static (int LineBreakStart, int DelimiterStart) FindDelimiterLine(ReadOnlySpan<byte> span, byte[] delimiter, int from)
    {
        var offset = from;
        while (offset < span.Length)
        {
            var index = span.Slice(offset).IndexOf(delimiter);
            if (index < 0)
            {
                break;
            }

            var delimiterStart = offset + index;
            if (delimiterStart > from && span[delimiterStart - 1] == (byte)'\n')
            {
                var lineBreakStart = delimiterStart - 1;
                if (lineBreakStart > from && span[lineBreakStart - 1] == (byte)'\r')
                {
                    lineBreakStart--;
                }

                return (lineBreakStart, delimiterStart);
            }

            offset = delimiterStart + 1;
        }

        return (-1, -1);
    }

    private static int SkipLineBreak(ReadOnlySpan<byte> span, int position)
    {
        if (span.Slice(position).StartsWith(CrLf))
        {
            return position + 2;
        }

        return position < span.Length && span[position] == (byte)'\n' ? position + 1 : -1;
    }

    private static string Latin1(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }

        return new string(chars);
    }

    private static void WriteAscii(MemoryStream stream, string text) => Write(stream, Encoding.ASCII.GetBytes(text));

    private static void Write(MemoryStream stream, ReadOnlySpan<byte> bytes)
    {
#if NET
        stream.Write(bytes);
#else
        var array = bytes.ToArray();
        stream.Write(array, 0, array.Length);
#endif
    }
}
