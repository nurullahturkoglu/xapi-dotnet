using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Xapi.Lrs.Http;

// POST to a document resource with JSON for an existing JSON document (IEEE 9274.1.1, 4.1.6.2 POST): every
// top-level property of the posted object replaces the property of the same name in the stored object, whole,
// even when it is an object itself. Properties keep the stored order; new ones are appended in posted order.
internal static class JsonDocumentMerge
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool IsObject(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // False when either side is not a JSON object
    public static bool TryMerge(ReadOnlyMemory<byte> stored, ReadOnlyMemory<byte> posted, out byte[] merged)
    {
        merged = [];
        JsonDocument? storedDocument = null;
        JsonDocument? postedDocument = null;
        try
        {
            storedDocument = JsonDocument.Parse(stored);
            postedDocument = JsonDocument.Parse(posted);
            if (storedDocument.RootElement.ValueKind != JsonValueKind.Object || postedDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = new List<KeyValuePair<string, JsonElement>>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in storedDocument.RootElement.EnumerateObject().Concat(postedDocument.RootElement.EnumerateObject()))
            {
                // A repeated name, in either document, keeps its first position and its last value
                if (positions.TryGetValue(property.Name, out var position))
                {
                    properties[position] = new(property.Name, property.Value);
                }
                else
                {
                    positions[property.Name] = properties.Count;
                    properties.Add(new(property.Name, property.Value));
                }
            }

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                foreach (var (name, value) in properties)
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            merged = buffer.WrittenSpan.ToArray();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            storedDocument?.Dispose();
            postedDocument?.Dispose();
        }
    }
}
