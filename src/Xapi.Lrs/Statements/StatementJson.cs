using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xapi.Json;

namespace Xapi.Lrs.Statements;

// Statements written as UTF-8 JSON, the form they are stored and sent in, without the string that
// XapiJson.SerializeStatement would make on the way. Same escaping as XapiJson.
internal static class StatementJson
{
    public static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static ReadOnlyMemory<byte> Write(Statement statement, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            XapiJson.WriteStatement(writer, statement, version, format, acceptLanguages);
        }

        return buffer.WrittenMemory;
    }
}
