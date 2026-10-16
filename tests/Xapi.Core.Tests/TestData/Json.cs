using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xapi.Json;

namespace Xapi.Core.Tests.TestData;

/// <summary>Builds test Statements as JSON and parses them.</summary>
internal static class Json
{
    public const string MinimalStatement = """
        {
          "actor": { "mbox": "mailto:learner@example.com" },
          "verb": { "id": "http://adlnet.gov/expapi/verbs/completed" },
          "object": { "id": "http://example.com/activities/1" }
        }
        """;

    public static JsonObject Minimal() => JsonNode.Parse(MinimalStatement)!.AsObject();

    public static JsonObject Minimal(Action<JsonObject> change)
    {
        var statement = Minimal();
        change(statement);
        return statement;
    }

    public static XapiParseResult<Statement> Parse(JsonNode statement, XapiVersion? version = null, XapiReadMode mode = XapiReadMode.Strict) =>
        XapiJson.ParseStatement(statement.ToJsonString(), version ?? XapiVersion.V2_0_0, mode);

    public static XapiParseResult<Statement> Parse(string json, XapiVersion? version = null, XapiReadMode mode = XapiReadMode.Strict) =>
        XapiJson.ParseStatement(json, version ?? XapiVersion.V2_0_0, mode);

    public static Statement ParseValid(string json, XapiVersion? version = null) => Parse(json, version).GetValueOrThrow();

    public static Statement ParseValid(JsonNode json, XapiVersion? version = null) => Parse(json, version).GetValueOrThrow();

    /// <summary>Asserts that reading fails with exactly one error of <paramref name="code"/> at <paramref name="path"/>.</summary>
    public static void AssertSingleError<T>(XapiParseResult<T> result, string code, string path)
    {
        Assert.False(result.IsSuccess, "Expected the input to be rejected.");
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(path, error.Path);
    }

    /// <summary>Asserts that reading fails with an error of <paramref name="code"/> at <paramref name="path"/> (among others).</summary>
    public static void AssertHasError<T>(XapiParseResult<T> result, string code, string path)
    {
        Assert.False(result.IsSuccess, "Expected the input to be rejected.");
        Assert.Contains(result.Errors, e => e.Code == code && e.Path == path);
    }

    public static Statement Valid() => ParseValid(MinimalStatement);

    /// <summary>Removes insignificant whitespace without touching property order, number text or escaping.</summary>
    public static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            document.RootElement.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
