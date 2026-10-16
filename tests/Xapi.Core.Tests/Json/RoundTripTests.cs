using Xapi.Json;
using static Xapi.Core.Tests.TestData.Json;

namespace Xapi.Core.Tests.Json;

/// <summary>
/// Each input is written in the writer's property order, so reading and writing it again must give the same text,
/// and reading that text must give an equal model.
/// </summary>
public class RoundTripTests
{
    private const string Head = """
        "actor":{"objectType":"Agent","mbox":"mailto:learner@example.com"},
        "verb":{"id":"http://adlnet.gov/expapi/verbs/completed"}
        """;

    public static TheoryData<string, string> Statements => new()
    {
        {
            "full statement",
            """
            {"id":"fd41c918-b88b-4b20-a0a5-a4c32391aaa0",
             "actor":{"objectType":"Agent","name":"Learner","mbox":"mailto:learner@example.com"},
             "verb":{"id":"http://adlnet.gov/expapi/verbs/completed","display":{"en-US":"completed","tr":"tamamladı"}},
             "object":{"objectType":"Activity","id":"http://example.com/activities/1"},
             "result":{"score":{"scaled":0.95,"raw":95,"min":0,"max":100},"success":true,"completion":true,"response":"done","duration":"PT1H2M3.45S",
                       "extensions":{"http://example.com/ext/r":{"a":[1,2.5e3,"x"],"b":null}}},
             "context":{"registration":"ec531277-b57b-4c15-8d91-d292c5b2b8f7",
                        "instructor":{"objectType":"Agent","name":"Teacher","account":{"homePage":"http://example.com","name":"t-1"}},
                        "team":{"objectType":"Group","name":"Team","member":[{"objectType":"Agent","openid":"http://openid.example.org/1"}]},
                        "contextActivities":{"parent":[{"id":"http://example.com/p"}],"grouping":[{"id":"http://example.com/g"}],
                                             "category":[{"id":"http://example.com/c"}],"other":[{"id":"http://example.com/o"}]},
                        "revision":"r1","platform":"Example LMS","language":"en-US",
                        "statement":{"objectType":"StatementRef","id":"6690e6c9-3ef0-4ed3-8b37-7f3964730bee"},
                        "extensions":{"http://example.com/ext/c":"value"}},
             "timestamp":"2026-10-16T12:00:00.123Z",
             "stored":"2026-10-16T12:00:01.000Z",
             "authority":{"objectType":"Agent","account":{"homePage":"http://lrs.example.com","name":"client"}},
             "version":"2.0.0",
             "attachments":[{"usageType":"http://adlnet.gov/expapi/attachments/signature","display":{"en-US":"Signature"},
                             "description":{"en-US":"A test signature"},"contentType":"application/octet-stream","length":4235,
                             "sha2":"672fa5fa658017f1b72d65036f13379c6ab05d4ab3b6664908d8acf0b6a0c634","fileUrl":"http://example.com/sig"}]}
            """
        },
        {
            "sha1 actor, statement ref object",
            """
            {"actor":{"objectType":"Agent","mbox_sha1sum":"ebd31e95054c018b10727ccffd2ef2ec3a016ee9"},
             "verb":{"id":"http://adlnet.gov/expapi/verbs/voided"},
             "object":{"objectType":"StatementRef","id":"e05aa883-acaf-40ad-bf54-02c8ce485fb0"}}
            """
        },
        {
            "identified group actor with members",
            """
            {"actor":{"objectType":"Group","name":"Team","account":{"homePage":"http://example.com","name":"team-1"},
                      "member":[{"objectType":"Agent","mbox":"mailto:a@example.com"},{"objectType":"Agent","mbox":"mailto:b@example.com"}]},
             "verb":{"id":"http://adlnet.gov/expapi/verbs/completed"},
             "object":{"objectType":"Activity","id":"http://example.com/activities/1"}}
            """
        },
        {
            "agent object",
            "{" + Head + "," + """
             "object":{"objectType":"Agent","openid":"http://openid.example.org/2"}}
            """
        },
        {
            "group object",
            "{" + Head + "," + """
             "object":{"objectType":"Group","mbox":"mailto:team@example.com"}}
            """
        },
        {
            "substatement",
            "{" + Head + "," + """
             "object":{"objectType":"SubStatement",
                       "actor":{"objectType":"Agent","mbox":"mailto:b@example.com"},
                       "verb":{"id":"http://example.com/verbs/will-visit","display":{"en-US":"will visit"}},
                       "object":{"objectType":"Activity","id":"http://example.com/place"},
                       "result":{"completion":false},
                       "context":{"contextActivities":{"parent":[{"id":"http://example.com/p"}]}},
                       "timestamp":"2026-11-01T09:00:00.000Z",
                       "attachments":[{"usageType":"http://example.com/u","display":{"en":"x"},"contentType":"text/plain","length":0,
                                       "sha2":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}]}}
            """
        },
        {
            "context agents and groups (2.0)",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/activities/1"},
             "context":{"contextAgents":[{"objectType":"contextAgent","agent":{"objectType":"Agent","mbox":"mailto:b@example.com"},
                                          "relevantTypes":["http://example.com/types/observer"]},
                                         {"objectType":"contextAgent","agent":{"objectType":"Agent","mbox":"mailto:c@example.com"}}],
                        "contextGroups":[{"objectType":"contextGroup","group":{"objectType":"Group","member":[{"objectType":"Agent","mbox":"mailto:d@example.com"}]},
                                          "relevantTypes":["http://example.com/types/class"]}]}}
            """
        },
        {
            "choice interaction",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/q1","definition":{
               "name":{"en-US":"Question 1"},"description":{"en-US":"Which colors?"},
               "type":"http://adlnet.gov/expapi/activities/cmi.interaction","moreInfo":"http://example.com/q1/info",
               "interactionType":"choice","correctResponsesPattern":["golf[,]tetris"],
               "choices":[{"id":"golf","description":{"en-US":"Golf Example"}},{"id":"tetris","description":{"en-US":"Tetris Example"}}],
               "extensions":{"http://example.com/ext/d":true}}}}
            """
        },
        {
            "likert interaction",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/q2","definition":{
               "interactionType":"likert","correctResponsesPattern":["likert_3"],
               "scale":[{"id":"likert_0"},{"id":"likert_3","description":{"en-US":"It's OK"}}]}}}
            """
        },
        {
            "matching interaction",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/q3","definition":{
               "interactionType":"matching","correctResponsesPattern":["ben[.]3"],
               "source":[{"id":"ben"}],"target":[{"id":"3"}]}}}
            """
        },
        {
            "performance interaction",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/q4","definition":{
               "interactionType":"performance","correctResponsesPattern":["pong[.]1:[,]dg[.]:10"],"steps":[{"id":"pong"},{"id":"dg"}]}}}
            """
        },
        {
            "fill-in with an empty correct response pattern",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/q5","definition":{"interactionType":"fill-in","correctResponsesPattern":[]}}}
            """
        },
        {
            "numeric, sequencing, true-false, long-fill-in and other",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/q6","definition":{"interactionType":"numeric","correctResponsesPattern":["4[:]"]}},
             "context":{"contextActivities":{"other":[
               {"id":"http://example.com/q7","definition":{"interactionType":"sequencing","choices":[{"id":"a"}]}},
               {"id":"http://example.com/q8","definition":{"interactionType":"true-false","correctResponsesPattern":["true"]}},
               {"id":"http://example.com/q9","definition":{"interactionType":"long-fill-in"}},
               {"id":"http://example.com/q10","definition":{"interactionType":"other"}}]}}}
            """
        },
        {
            "scores at the edges",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/activities/1"},
             "result":{"score":{"scaled":-1,"raw":-0.5,"min":-1,"max":1.5}}}
            """
        },
        {
            "1.0.3 statement with sub-millisecond timestamp",
            "{" + Head + "," + """
             "object":{"objectType":"Activity","id":"http://example.com/activities/1"},
             "timestamp":"2026-10-16T12:00:00.1234567Z","version":"1.0.3"}
            """
        },
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void Statements_round_trip(string name, string json)
    {
        _ = name;
        var only2 = json.Contains("contextAgents", StringComparison.Ordinal) || json.Contains("\"2.0.0\"", StringComparison.Ordinal);
        var version = only2 ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3;
        foreach (var profile in new[] { version, XapiVersion.V2_0_0 })
        {
            var statement = ParseValid(json, profile);
            var written = XapiJson.SerializeStatement(statement, profile);
            Assert.Equal(Compact(json), written);
            Assert.Equal(statement, ParseValid(written, profile));
        }
    }

    [Theory]
    [InlineData("""{"objectType":"Agent","name":"A","mbox":"mailto:a@example.com"}""")]
    [InlineData("""{"objectType":"Agent","mbox_sha1sum":"ebd31e95054c018b10727ccffd2ef2ec3a016ee9"}""")]
    [InlineData("""{"objectType":"Agent","openid":"http://openid.example.org/1"}""")]
    [InlineData("""{"objectType":"Agent","account":{"homePage":"http://example.com","name":"1"}}""")]
    [InlineData("""{"objectType":"Group","name":"G","member":[{"objectType":"Agent","mbox":"mailto:a@example.com"}]}""")]
    [InlineData("""{"objectType":"Group","openid":"http://openid.example.org/g"}""")]
    public void Actors_round_trip(string json)
    {
        var actor = XapiJson.ParseActor(json, XapiVersion.V2_0_0).GetValueOrThrow();
        var written = XapiJson.SerializeActor(actor, XapiVersion.V2_0_0);
        Assert.Equal(json, written);
        Assert.Equal(actor, XapiJson.ParseActor(written, XapiVersion.V2_0_0).GetValueOrThrow());
    }

    [Fact]
    public void Activity_round_trips()
    {
        const string Json = """{"objectType":"Activity","id":"http://example.com/a","definition":{"name":{"en":"A","de":"B"},"type":"http://example.com/t"}}""";
        var activity = XapiJson.ParseActivity(Json, XapiVersion.V2_0_0).GetValueOrThrow();
        Assert.Equal(Json, XapiJson.SerializeActivity(activity, XapiVersion.V2_0_0));
    }

    [Fact]
    public void Person_round_trips()
    {
        const string Json = """
            {"objectType":"Person","name":["Andrew Downes","Toby Nichols"],"mbox":["mailto:andrew@example.com"],
             "mbox_sha1sum":["ebd31e95054c018b10727ccffd2ef2ec3a016ee9"],"openid":["http://openid.example.org/1"],
             "account":[{"homePage":"http://www.example.com","name":"13936749"}]}
            """;
        var person = XapiJson.ParsePerson(Json, XapiVersion.V2_0_0).GetValueOrThrow();
        var written = XapiJson.SerializePerson(person, XapiVersion.V2_0_0);
        Assert.Equal(Compact(Json), written);
        Assert.Equal(person, XapiJson.ParsePerson(written, XapiVersion.V2_0_0).GetValueOrThrow());
    }

    [Fact]
    public void About_round_trips()
    {
        const string Json = """{"version":["1.0.3","2.0.0"],"extensions":{"http://example.com/lrs":{"name":"test"}}}""";
        var about = XapiJson.ParseAbout(Json, XapiVersion.V2_0_0).GetValueOrThrow();
        var written = XapiJson.SerializeAbout(about, XapiVersion.V2_0_0);
        Assert.Equal(Json, written);
        Assert.Equal(about, XapiJson.ParseAbout(written, XapiVersion.V2_0_0).GetValueOrThrow());
    }

    [Fact]
    public void Statement_arrays_round_trip()
    {
        var json = "[" + Compact(MinimalStatement) + "," + Compact(MinimalStatement) + "]";
        var statements = XapiJson.ParseStatements(json, XapiVersion.V2_0_0).GetValueOrThrow();
        var written = XapiJson.SerializeStatements(statements, XapiVersion.V2_0_0);
        Assert.Equal(json.Replace("{\"mbox\"", "{\"objectType\":\"Agent\",\"mbox\"", StringComparison.Ordinal).Replace("\"object\":{\"id\"", "\"object\":{\"objectType\":\"Activity\",\"id\"", StringComparison.Ordinal), written);
    }
}
