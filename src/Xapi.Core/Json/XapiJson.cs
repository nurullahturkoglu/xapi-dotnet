using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xapi.Internal;
using Xapi.Validation;

namespace Xapi.Json;

/// <summary>
/// Reads and writes xAPI JSON without reflection or source generators, so it works under trimming, NativeAOT,
/// and IL2CPP.
/// </summary>
/// <remarks>
/// <para>
/// Reading collects every error with a JSON path instead of stopping at the first. In
/// <see cref="XapiReadMode.Strict"/> mode the result is also validated with <see cref="StatementValidator"/>;
/// validation runs only when the JSON could be turned into a model, so a document with structural errors
/// reports those first.
/// </para>
/// <para>
/// Writing emits only properties the standard defines, timestamps in UTC with fixed fraction digits, and
/// extension values exactly as read. Agents and Groups always carry <c>objectType</c>; Activities carry it in
/// the object position only.
/// </para>
/// </remarks>
public static class XapiJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads one Statement.</summary>
    /// <param name="utf8Json">UTF-8 JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Statement, or every error found.</returns>
    public static XapiParseResult<Statement> ParseStatement(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Parse(utf8Json, version, mode, (reader, root) => ReadStatement(reader, root, mode));

    /// <summary>Reads one Statement.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Statement, or every error found.</returns>
    public static XapiParseResult<Statement> ParseStatement(string json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        ParseStatement(Utf8(json), version, mode);

    /// <summary>Reads one Statement from an already parsed JSON value.</summary>
    /// <param name="json">The JSON value.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Statement, or every error found.</returns>
    public static XapiParseResult<Statement> ParseStatement(JsonElement json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Finish(new JsonModelReader(version, mode), json, (reader, root) => ReadStatement(reader, root, mode));

    /// <summary>Reads a Statement batch: a JSON array of Statements, or a single Statement object.</summary>
    /// <remarks>In strict mode the batch is validated with <see cref="StatementValidator.ValidateBatch"/>, which also rejects duplicate ids.</remarks>
    /// <param name="utf8Json">UTF-8 JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Statements, or every error found. Paths start with the array index, for example <c>$[2].actor</c>.</returns>
    public static XapiParseResult<XapiList<Statement>> ParseStatements(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Parse(utf8Json, version, mode, (reader, root) => ReadStatements(reader, root, mode));

    /// <summary>Reads a Statement batch: a JSON array of Statements, or a single Statement object.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Statements, or every error found.</returns>
    public static XapiParseResult<XapiList<Statement>> ParseStatements(string json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        ParseStatements(Utf8(json), version, mode);

    /// <summary>Reads a Statement batch from an already parsed JSON value (an array or a single object).</summary>
    /// <param name="json">The JSON value.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Statements, or every error found.</returns>
    public static XapiParseResult<XapiList<Statement>> ParseStatements(JsonElement json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Finish(new JsonModelReader(version, mode), json, (reader, root) => ReadStatements(reader, root, mode));

    /// <summary>Reads an Agent or Group, for example the <c>agent</c> query parameter.</summary>
    /// <param name="utf8Json">UTF-8 JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Actor, or every error found.</returns>
    public static XapiParseResult<Actor> ParseActor(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Parse(utf8Json, version, mode, (reader, root) => Validated(reader, reader.ReadActorRoot(root, reader.Root), mode, StatementValidator.ValidateActor));

    /// <summary>Reads an Agent or Group.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Actor, or every error found.</returns>
    public static XapiParseResult<Actor> ParseActor(string json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        ParseActor(Utf8(json), version, mode);

    /// <summary>Reads an Activity, for example a response of the Activities resource.</summary>
    /// <param name="utf8Json">UTF-8 JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Activity, or every error found.</returns>
    public static XapiParseResult<Activity> ParseActivity(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Parse(utf8Json, version, mode, (reader, root) => Validated(reader, reader.ReadActivity(root, reader.Root), mode, StatementValidator.ValidateActivity));

    /// <summary>Reads an Activity.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Activity, or every error found.</returns>
    public static XapiParseResult<Activity> ParseActivity(string json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        ParseActivity(Utf8(json), version, mode);

    /// <summary>Reads a Person object (a response of the Agents resource).</summary>
    /// <param name="utf8Json">UTF-8 JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Person, or every error found.</returns>
    public static XapiParseResult<Person> ParsePerson(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Parse(utf8Json, version, mode, (reader, root) => Validated(reader, reader.ReadPerson(root, reader.Root), mode, StatementValidator.ValidatePerson));

    /// <summary>Reads a Person object.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The Person, or every error found.</returns>
    public static XapiParseResult<Person> ParsePerson(string json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        ParsePerson(Utf8(json), version, mode);

    /// <summary>Reads an About object (the response of the About resource).</summary>
    /// <param name="utf8Json">UTF-8 JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read. Use <see cref="XapiReadMode.Lenient"/> for responses of unknown LRSs.</param>
    /// <returns>The About object, or every error found.</returns>
    public static XapiParseResult<About> ParseAbout(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        Parse(utf8Json, version, mode, (reader, root) => reader.ReadAbout(root, reader.Root));

    /// <summary>Reads an About object.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="mode">How strictly to read.</param>
    /// <returns>The About object, or every error found.</returns>
    public static XapiParseResult<About> ParseAbout(string json, XapiVersion version, XapiReadMode mode = XapiReadMode.Strict) =>
        ParseAbout(Utf8(json), version, mode);

    /// <summary>Writes one Statement.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="statement">The Statement.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <param name="format">Which parts of Agents, Groups, Activities and Verbs to include.</param>
    /// <param name="acceptLanguages">For <see cref="StatementFormat.Canonical"/>: language ranges in order of preference, like an <c>Accept-Language</c> header.</param>
    /// <exception cref="InvalidOperationException">The Statement uses <c>contextAgents</c> or <c>contextGroups</c> and <paramref name="version"/> is 1.0.3.</exception>
    public static void WriteStatement(Utf8JsonWriter writer, Statement statement, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null)
    {
        Guard.NotNull(writer);
        Guard.NotNull(statement);
        new JsonModelWriter(writer, version, format, acceptLanguages).WriteStatement(statement);
    }

    /// <summary>Writes Statements as a JSON array.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="statements">The Statements.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <param name="format">Which parts of Agents, Groups, Activities and Verbs to include.</param>
    /// <param name="acceptLanguages">For <see cref="StatementFormat.Canonical"/>: language ranges in order of preference.</param>
    /// <exception cref="InvalidOperationException">A Statement uses <c>contextAgents</c> or <c>contextGroups</c> and <paramref name="version"/> is 1.0.3.</exception>
    public static void WriteStatements(Utf8JsonWriter writer, IEnumerable<Statement> statements, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null)
    {
        Guard.NotNull(writer);
        Guard.NotNull(statements);
        var modelWriter = new JsonModelWriter(writer, version, format, acceptLanguages);
        writer.WriteStartArray();
        foreach (var statement in statements)
        {
            modelWriter.WriteStatement(Guard.NotNull(statement));
        }

        writer.WriteEndArray();
    }

    /// <summary>Serializes one Statement to a JSON string.</summary>
    /// <param name="statement">The Statement.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <param name="format">Which parts of Agents, Groups, Activities and Verbs to include.</param>
    /// <param name="acceptLanguages">For <see cref="StatementFormat.Canonical"/>: language ranges in order of preference.</param>
    /// <returns>Compact JSON text.</returns>
    /// <exception cref="InvalidOperationException">The Statement uses <c>contextAgents</c> or <c>contextGroups</c> and <paramref name="version"/> is 1.0.3.</exception>
    public static string SerializeStatement(Statement statement, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null) =>
        Serialize(writer => WriteStatement(writer, statement, version, format, acceptLanguages));

    /// <summary>Serializes Statements to a JSON array string.</summary>
    /// <param name="statements">The Statements.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <param name="format">Which parts of Agents, Groups, Activities and Verbs to include.</param>
    /// <param name="acceptLanguages">For <see cref="StatementFormat.Canonical"/>: language ranges in order of preference.</param>
    /// <returns>Compact JSON text.</returns>
    /// <exception cref="InvalidOperationException">A Statement uses <c>contextAgents</c> or <c>contextGroups</c> and <paramref name="version"/> is 1.0.3.</exception>
    public static string SerializeStatements(IEnumerable<Statement> statements, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null) =>
        Serialize(writer => WriteStatements(writer, statements, version, format, acceptLanguages));

    /// <summary>Writes an Agent or Group.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="actor">The Actor.</param>
    /// <param name="version">The xAPI version to write for.</param>
    public static void WriteActor(Utf8JsonWriter writer, Actor actor, XapiVersion version)
    {
        Guard.NotNull(writer);
        Guard.NotNull(actor);
        new JsonModelWriter(writer, version, StatementFormat.Exact, null).WriteActor(actor);
    }

    /// <summary>Serializes an Agent or Group, for example for the <c>agent</c> query parameter.</summary>
    /// <param name="actor">The Actor.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <returns>Compact JSON text.</returns>
    public static string SerializeActor(Actor actor, XapiVersion version) => Serialize(writer => WriteActor(writer, actor, version));

    /// <summary>Writes an Activity (with <c>objectType</c>).</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="activity">The Activity.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <param name="format">Use <see cref="StatementFormat.Canonical"/> with <paramref name="acceptLanguages"/> to keep one language per map.</param>
    /// <param name="acceptLanguages">Language ranges in order of preference.</param>
    public static void WriteActivity(Utf8JsonWriter writer, Activity activity, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null)
    {
        Guard.NotNull(writer);
        Guard.NotNull(activity);
        new JsonModelWriter(writer, version, format, acceptLanguages).WriteActivity(activity, writeObjectType: true);
    }

    /// <summary>Serializes an Activity (with <c>objectType</c>).</summary>
    /// <param name="activity">The Activity.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <param name="format">Use <see cref="StatementFormat.Canonical"/> with <paramref name="acceptLanguages"/> to keep one language per map.</param>
    /// <param name="acceptLanguages">Language ranges in order of preference.</param>
    /// <returns>Compact JSON text.</returns>
    public static string SerializeActivity(Activity activity, XapiVersion version, StatementFormat format = StatementFormat.Exact, IReadOnlyList<string>? acceptLanguages = null) =>
        Serialize(writer => WriteActivity(writer, activity, version, format, acceptLanguages));

    /// <summary>Writes a Person object.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="person">The Person.</param>
    /// <param name="version">The xAPI version to write for.</param>
    public static void WritePerson(Utf8JsonWriter writer, Person person, XapiVersion version)
    {
        Guard.NotNull(writer);
        Guard.NotNull(person);
        new JsonModelWriter(writer, version, StatementFormat.Exact, null).WritePerson(person);
    }

    /// <summary>Serializes a Person object.</summary>
    /// <param name="person">The Person.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <returns>Compact JSON text.</returns>
    public static string SerializePerson(Person person, XapiVersion version) => Serialize(writer => WritePerson(writer, person, version));

    /// <summary>Writes an About object.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="about">The About object.</param>
    /// <param name="version">The xAPI version to write for.</param>
    public static void WriteAbout(Utf8JsonWriter writer, About about, XapiVersion version)
    {
        Guard.NotNull(writer);
        Guard.NotNull(about);
        new JsonModelWriter(writer, version, StatementFormat.Exact, null).WriteAbout(about);
    }

    /// <summary>Serializes an About object.</summary>
    /// <param name="about">The About object.</param>
    /// <param name="version">The xAPI version to write for.</param>
    /// <returns>Compact JSON text.</returns>
    public static string SerializeAbout(About about, XapiVersion version) => Serialize(writer => WriteAbout(writer, about, version));

    private delegate T? ReadRoot<T>(JsonModelReader reader, JsonElement root);

    private static Statement? ReadStatement(JsonModelReader reader, JsonElement root, XapiReadMode mode) =>
        Validated(reader, reader.ReadStatement(root, reader.Root), mode, StatementValidator.Validate);

    private static XapiList<Statement>? ReadStatements(JsonModelReader reader, JsonElement root, XapiReadMode mode)
    {
        XapiList<Statement>? statements;
        if (root.ValueKind == JsonValueKind.Object)
        {
            statements = reader.ReadStatement(root, reader.Root) is { } single ? XapiList.Create(single) : null;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            var start = reader.Errors.Count;
            var items = new List<Statement>();
            var index = 0;
            foreach (var item in root.EnumerateArray())
            {
                if (reader.ReadStatement(item, reader.Root.Index(index++)) is { } statement)
                {
                    items.Add(statement);
                }
            }

            statements = reader.Errors.Count > start ? null : XapiList.CreateRange(items);
        }
        else
        {
            reader.Errors.Add(new XapiError(XapiErrorCodes.JsonType, JsonPath.Root, "Expected a Statement object or an array of Statements."));
            return null;
        }

        if (statements is not null && mode == XapiReadMode.Strict)
        {
            var errors = root.ValueKind == JsonValueKind.Array
                ? StatementValidator.ValidateBatch(statements, reader.Version)
                : StatementValidator.Validate(statements[0], reader.Version);
            reader.Errors.AddRange(errors);
        }

        return reader.Errors.Count > 0 ? null : statements;
    }

    private static T? Validated<T>(JsonModelReader reader, T? value, XapiReadMode mode, Func<T, XapiVersion, IReadOnlyList<XapiError>> validate)
        where T : class
    {
        if (value is not null && mode == XapiReadMode.Strict)
        {
            reader.Errors.AddRange(validate(value, reader.Version));
        }

        return reader.Errors.Count > 0 ? null : value;
    }

    private static XapiParseResult<T> Parse<T>(ReadOnlySpan<byte> utf8Json, XapiVersion version, XapiReadMode mode, ReadRoot<T> read)
        where T : class
    {
        JsonDocument document;
        try
        {
            var jsonReader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
            document = JsonDocument.ParseValue(ref jsonReader);
            if (jsonReader.Read())
            {
                document.Dispose();
                return new XapiParseResult<T>([new XapiError(XapiErrorCodes.JsonSyntax, JsonPath.Root, "Unexpected content after the JSON value.")]);
            }
        }
        catch (JsonException exception)
        {
            return new XapiParseResult<T>([new XapiError(XapiErrorCodes.JsonSyntax, JsonPath.Root, exception.Message)]);
        }

        using (document)
        {
            return Finish(new JsonModelReader(version, mode), document.RootElement, read);
        }
    }

    private static XapiParseResult<T> Finish<T>(JsonModelReader reader, JsonElement root, ReadRoot<T> read)
        where T : class
    {
        var value = read(reader, root);
        return value is not null && reader.Errors.Count == 0
            ? new XapiParseResult<T>(value)
            : new XapiParseResult<T>(reader.Errors.Count > 0 ? reader.Errors : [new XapiError(XapiErrorCodes.JsonType, JsonPath.Root, "The JSON could not be read.")]);
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(Guard.NotNull(json));

    private static string Serialize(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}
