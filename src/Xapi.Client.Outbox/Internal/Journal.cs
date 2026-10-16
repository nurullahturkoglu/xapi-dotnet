using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Xapi.Json;

namespace Xapi.Client.Outbox.Internal;

/// <summary>
/// Encodes and decodes journal lines: <c>{"crc":"&lt;8 hex digits&gt;","record":{...}}</c> followed by <c>\n</c>,
/// where the CRC-32 covers the exact UTF-8 bytes of the record object.
/// </summary>
/// <remarks>
/// Statements are written as xAPI 2.0.0 JSON, whose model is a superset of 1.0.3, and read back leniently. The
/// version used to send them is decided later, by the client.
/// </remarks>
internal static class Journal
{
    private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("{\"crc\":\"");
    private static readonly byte[] Middle = Encoding.ASCII.GetBytes("\",\"record\":");
    private static readonly byte[] Suffix = Encoding.ASCII.GetBytes("}\n");

    public static byte[] Encode(JournalRecord record)
    {
        var body = EncodeBody(record);
        var crc = Encoding.ASCII.GetBytes(Crc32.Compute(body).ToString("x8", CultureInfo.InvariantCulture));
        var line = new byte[Prefix.Length + crc.Length + Middle.Length + body.Length + Suffix.Length];
        var offset = 0;
        foreach (var part in new[] { Prefix, crc, Middle, body, Suffix })
        {
            Buffer.BlockCopy(part, 0, line, offset, part.Length);
            offset += part.Length;
        }

        return line;
    }

    /// <summary>Decodes one line without its <c>\n</c>.</summary>
    /// <returns>The record, or <see langword="null"/> when the line is damaged or not understood.</returns>
    public static JournalRecord? Decode(ReadOnlySpan<byte> line)
    {
        try
        {
            var reader = new Utf8JsonReader(line);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject
                || !reader.Read() || !reader.ValueTextEquals("crc")
                || !reader.Read() || reader.TokenType != JsonTokenType.String
                || !uint.TryParse(reader.GetString(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var crc)
                || !reader.Read() || !reader.ValueTextEquals("record")
                || !reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            var end = (int)reader.BytesConsumed;
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return null;
            }

            var body = line.Slice(start, end - start);
            if (Crc32.Compute(body) != crc)
            {
                return null;
            }

            using var document = JsonDocument.Parse(body.ToArray());
            return DecodeBody(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static byte[] EncodeBody(JournalRecord record)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            switch (record)
            {
                case AddRecord add:
                    writer.WriteString("op", "add");
                    WriteEntry(writer, add.Entry);
                    break;
                case RemoveRecord remove:
                    writer.WriteString("op", "remove");
                    WriteIds(writer, remove.Ids);
                    break;
                case AttemptRecord attempt:
                    writer.WriteString("op", "attempt");
                    WriteIds(writer, attempt.Ids);
                    writer.WriteString("error", attempt.Error);
                    break;
                case DeadLetterRecord dead:
                    writer.WriteString("op", "dead");
                    WriteDeadLetter(writer, dead.DeadLetter);
                    break;
                case RequeueRecord requeue:
                    writer.WriteString("op", "requeue");
                    writer.WriteString("id", requeue.Id);
                    break;
                case RemoveDeadLetterRecord removeDead:
                    writer.WriteString("op", "removeDead");
                    writer.WriteString("id", removeDead.Id);
                    break;
                default:
                    throw new ArgumentException($"Unknown journal record {record.GetType().Name}.", nameof(record));
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static JournalRecord? DecodeBody(JsonElement record) =>
        record.GetProperty("op").GetString() switch
        {
            "add" => ReadEntry(record) is { } entry ? new AddRecord(entry) : null,
            "remove" => new RemoveRecord(ReadIds(record)),
            "attempt" => new AttemptRecord(ReadIds(record), record.GetProperty("error").GetString()!),
            "dead" => ReadDeadLetter(record) is { } deadLetter ? new DeadLetterRecord(deadLetter) : null,
            "requeue" => new RequeueRecord(record.GetProperty("id").GetGuid()),
            "removeDead" => new RemoveDeadLetterRecord(record.GetProperty("id").GetGuid()),
            _ => null,
        };

    private static void WriteEntry(Utf8JsonWriter writer, OutboxEntry entry)
    {
        writer.WriteString("enqueuedAt", entry.EnqueuedAt);
        writer.WriteNumber("attempts", entry.Attempts);
        if (entry.LastError is not null)
        {
            writer.WriteString("lastError", entry.LastError);
        }

        writer.WritePropertyName("statement");
        XapiJson.WriteStatement(writer, entry.Statement, XapiVersion.V2_0_0);
    }

    private static OutboxEntry? ReadEntry(JsonElement record) =>
        ReadStatement(record) is { } statement
            ? new OutboxEntry(statement, record.GetProperty("enqueuedAt").GetDateTimeOffset())
            {
                Attempts = record.GetProperty("attempts").GetInt32(),
                LastError = record.TryGetProperty("lastError", out var error) ? error.GetString() : null,
            }
            : null;

    private static void WriteDeadLetter(Utf8JsonWriter writer, OutboxDeadLetter deadLetter)
    {
        writer.WriteString("reason", ReasonName(deadLetter.Reason));
        writer.WriteString("error", deadLetter.Error);
        if (deadLetter.StatusCode is { } status)
        {
            writer.WriteNumber("statusCode", (int)status);
        }

        if (deadLetter.ResponseBody is not null)
        {
            writer.WriteString("responseBody", deadLetter.ResponseBody);
        }

        writer.WriteString("enqueuedAt", deadLetter.EnqueuedAt);
        writer.WriteString("deadLetteredAt", deadLetter.DeadLetteredAt);
        writer.WriteNumber("attempts", deadLetter.Attempts);
        writer.WritePropertyName("statement");
        XapiJson.WriteStatement(writer, deadLetter.Statement, XapiVersion.V2_0_0);
    }

    private static OutboxDeadLetter? ReadDeadLetter(JsonElement record)
    {
        if (ReadStatement(record) is not { } statement || ParseReason(record.GetProperty("reason").GetString()) is not { } reason)
        {
            return null;
        }

        return new OutboxDeadLetter(statement, reason, record.GetProperty("error").GetString()!, record.GetProperty("deadLetteredAt").GetDateTimeOffset())
        {
            StatusCode = record.TryGetProperty("statusCode", out var status) ? (HttpStatusCode)status.GetInt32() : null,
            ResponseBody = record.TryGetProperty("responseBody", out var body) ? body.GetString() : null,
            EnqueuedAt = record.GetProperty("enqueuedAt").GetDateTimeOffset(),
            Attempts = record.GetProperty("attempts").GetInt32(),
        };
    }

    private static Statement? ReadStatement(JsonElement record)
    {
        var parsed = XapiJson.ParseStatement(record.GetProperty("statement"), XapiVersion.V2_0_0, XapiReadMode.Lenient);
        return parsed.IsSuccess && parsed.Value.Id is not null ? parsed.Value : null;
    }

    private static void WriteIds(Utf8JsonWriter writer, IReadOnlyList<Guid> ids)
    {
        writer.WriteStartArray("ids");
        foreach (var id in ids)
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
    }

    private static List<Guid> ReadIds(JsonElement record)
    {
        var array = record.GetProperty("ids");
        var ids = new List<Guid>(array.GetArrayLength());
        foreach (var id in array.EnumerateArray())
        {
            ids.Add(id.GetGuid());
        }

        return ids;
    }

    // Spelled out instead of Enum.ToString so renaming a member cannot change the file format
    private static string ReasonName(OutboxDeadLetterReason reason) => reason switch
    {
        OutboxDeadLetterReason.Rejected => "rejected",
        OutboxDeadLetterReason.TooLarge => "tooLarge",
        OutboxDeadLetterReason.Conflict => "conflict",
        OutboxDeadLetterReason.MaxAttemptsExceeded => "maxAttemptsExceeded",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static OutboxDeadLetterReason? ParseReason(string? name) => name switch
    {
        "rejected" => OutboxDeadLetterReason.Rejected,
        "tooLarge" => OutboxDeadLetterReason.TooLarge,
        "conflict" => OutboxDeadLetterReason.Conflict,
        "maxAttemptsExceeded" => OutboxDeadLetterReason.MaxAttemptsExceeded,
        _ => null,
    };
}
