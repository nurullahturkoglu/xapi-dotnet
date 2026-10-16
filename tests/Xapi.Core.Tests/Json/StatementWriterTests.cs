using System.Text.Json.Nodes;
using Xapi.Json;
using static Xapi.Core.Tests.TestData.Json;

namespace Xapi.Core.Tests.Json;

public class StatementWriterTests
{
    private const string Rich = """
        {"actor":{"objectType":"Agent","name":"Learner","mbox":"mailto:learner@example.com"},
         "verb":{"id":"http://adlnet.gov/expapi/verbs/completed","display":{"en-US":"completed","de":"abgeschlossen"}},
         "object":{"objectType":"Activity","id":"http://example.com/q1","definition":{
           "name":{"en-US":"Question","de":"Frage"},"interactionType":"choice","correctResponsesPattern":["a"],
           "choices":[{"id":"a","description":{"en-US":"Yes","de":"Ja"}}]}},
         "context":{"instructor":{"objectType":"Group","name":"Teachers","mbox":"mailto:teachers@example.com",
                                  "member":[{"objectType":"Agent","name":"T","mbox":"mailto:t@example.com"}]},
                    "team":{"objectType":"Group","name":"Team","member":[{"objectType":"Agent","name":"M","mbox":"mailto:m@example.com"}]},
                    "contextActivities":{"parent":[{"id":"http://example.com/p","definition":{"name":{"en-US":"Parent"}}}]}}}
        """;

    [Fact]
    public void Ids_format_keeps_only_identifiers()
    {
        var written = XapiJson.SerializeStatement(ParseValid(Rich), XapiVersion.V2_0_0, StatementFormat.Ids);
        Assert.Equal(
            Compact("""
                {"actor":{"objectType":"Agent","mbox":"mailto:learner@example.com"},
                 "verb":{"id":"http://adlnet.gov/expapi/verbs/completed"},
                 "object":{"id":"http://example.com/q1"},
                 "context":{"instructor":{"objectType":"Group","mbox":"mailto:teachers@example.com"},
                            "team":{"objectType":"Group","member":[{"objectType":"Agent","mbox":"mailto:m@example.com"}]},
                            "contextActivities":{"parent":[{"id":"http://example.com/p"}]}}}
                """),
            written);
    }

    [Theory]
    [InlineData(null, "en-US", "completed", "Question", "Yes")]
    [InlineData("de", "de", "abgeschlossen", "Frage", "Ja")]
    [InlineData("de-AT", "de", "abgeschlossen", "Frage", "Ja")]
    [InlineData("fr", "en-US", "completed", "Question", "Yes")]
    public void Canonical_format_keeps_one_language(string? accept, string tag, string verb, string name, string choice)
    {
        string[]? languages = accept is null ? null : [accept];
        var written = JsonNode.Parse(XapiJson.SerializeStatement(ParseValid(Rich), XapiVersion.V2_0_0, StatementFormat.Canonical, languages))!;
        Assert.Equal(verb, Assert.Single(written["verb"]!["display"]!.AsObject()).Value!.GetValue<string>());
        Assert.Equal(tag, Assert.Single(written["verb"]!["display"]!.AsObject()).Key);
        Assert.Equal(name, Assert.Single(written["object"]!["definition"]!["name"]!.AsObject()).Value!.GetValue<string>());
        Assert.Equal(choice, Assert.Single(written["object"]!["definition"]!["choices"]![0]!["description"]!.AsObject()).Value!.GetValue<string>());
        Assert.Equal("Learner", written["actor"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Exact_format_writes_every_language()
    {
        var written = JsonNode.Parse(XapiJson.SerializeStatement(ParseValid(Rich), XapiVersion.V2_0_0))!;
        Assert.Equal(2, written["verb"]!["display"]!.AsObject().Count);
    }

    [Fact]
    public void Context_agents_cannot_be_written_as_1_0_3()
    {
        var statement = Valid() with
        {
            Context = new Context { ContextAgents = [new ContextAgent(Agent.FromMbox("b@example.com"))] },
        };
        Assert.Throws<InvalidOperationException>(() => XapiJson.SerializeStatement(statement, XapiVersion.V1_0_3));
        Assert.Contains("contextAgents", XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), StringComparison.Ordinal);
    }

    [Fact]
    public void Models_built_in_code_are_written_with_object_types_and_utc_timestamps()
    {
        var statement = new Statement(
            Agent.FromMbox("learner@example.com", "Learner"),
            Verbs.Completed,
            new Activity(new Iri("http://example.com/activities/1")))
        {
            Id = Guid.Parse("fd41c918-b88b-4b20-a0a5-a4c32391aaa0"),
            Result = new Result { Score = new Score { Scaled = 0.95 }, Duration = XapiDuration.FromTimeSpan(TimeSpan.FromMinutes(90)) },
            Timestamp = new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.FromHours(3)),
        };
        Assert.Equal(
            Compact("""
                {"id":"fd41c918-b88b-4b20-a0a5-a4c32391aaa0",
                 "actor":{"objectType":"Agent","name":"Learner","mbox":"mailto:learner@example.com"},
                 "verb":{"id":"http://adlnet.gov/expapi/verbs/completed","display":{"en-US":"completed"}},
                 "object":{"objectType":"Activity","id":"http://example.com/activities/1"},
                 "result":{"score":{"scaled":0.95},"duration":"PT1H30M"},
                 "timestamp":"2026-10-16T12:00:00.000Z"}
                """),
            XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0));
    }

    [Fact]
    public void Non_ascii_text_is_written_unescaped()
    {
        var statement = Valid() with { Verb = new Verb(new Iri("http://example.com/v")) { Display = LanguageMap.Create("tr", "tamamladı \"tamam\"") } };
        Assert.Contains("\"tr\":\"tamamladı \\\"tamam\\\"\"", XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteStatement_uses_the_given_writer()
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            XapiJson.WriteStatements(writer, [Valid()], XapiVersion.V2_0_0);
        }

        Assert.StartsWith("[{\"actor\":", System.Text.Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }
}
