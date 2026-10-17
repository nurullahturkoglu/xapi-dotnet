using System.Text.Json;

namespace Xapi.Lrs.Http;

internal static class StatementBatch
{
    // Whether the body is a JSON array of more than max elements. Only the top level is read, element by element,
    // so a batch can be turned away before parsing it allocates many times its size. A body that is not valid
    // JSON is left to the parser to report.
    public static bool IsLargerThan(byte[] json, int max)
    {
        var reader = new Utf8JsonReader(json);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return false;
            }

            var count = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (++count > max)
                {
                    return true;
                }

                reader.Skip();
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
