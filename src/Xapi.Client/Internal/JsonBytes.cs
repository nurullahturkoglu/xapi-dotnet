using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Xapi.Client.Internal;

internal static class JsonBytes
{
    // The same escaping as XapiJson: non-ASCII text stays readable on the wire
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static byte[] From(JsonElement json)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            json.WriteTo(writer);
        }

        return stream.ToArray();
    }

    public static byte[] From(string json) => Encoding.UTF8.GetBytes(json);
}
