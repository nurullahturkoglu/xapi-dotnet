using Xapi.Core.Tests.TestData;
using Xapi.Json;
using Xapi.Validation;
using static Xapi.Core.Tests.TestData.Json;

namespace Xapi.Core.Tests.Json;

public class SpecExampleTests
{
    public static TheoryData<string> Statements => [SpecExamples.SimpleStatement, SpecExamples.AttemptedStatement, SpecExamples.LongStatement];

    public static TheoryData<string> Objects => [SpecExamples.ActivityObject, SpecExamples.AgentObject, SpecExamples.GroupObject, SpecExamples.SubStatementObject];

    public static TheoryData<string, InteractionType, int> Definitions => new()
    {
        { SpecExamples.TrueFalseDefinition, InteractionType.TrueFalse, 0 },
        { SpecExamples.ChoiceDefinition, InteractionType.Choice, 4 },
        { SpecExamples.FillInDefinition, InteractionType.FillIn, 0 },
        { SpecExamples.LongFillInDefinition, InteractionType.LongFillIn, 0 },
        { SpecExamples.LikertDefinition, InteractionType.Likert, 4 },
        { SpecExamples.MatchingDefinition, InteractionType.Matching, 8 },
        { SpecExamples.PerformanceDefinition, InteractionType.Performance, 3 },
        { SpecExamples.SequencingDefinition, InteractionType.Sequencing, 4 },
        { SpecExamples.NumericDefinition, InteractionType.Numeric, 0 },
        { SpecExamples.OtherDefinition, InteractionType.Other, 0 },
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void Example_statements_are_valid_in_both_versions_and_round_trip(string json)
    {
        foreach (var version in new[] { XapiVersion.V1_0_3, XapiVersion.V2_0_0 })
        {
            var statement = ParseValid(json, version);
            var written = XapiJson.SerializeStatement(statement, version);
            Assert.Equal(statement, ParseValid(written, version));
            Assert.True(StatementEquivalence.AreEquivalent(statement, ParseValid(XapiJson.SerializeStatement(statement, version, StatementFormat.Canonical), version)));
        }
    }

    [Fact]
    public void Attempted_example_keeps_its_duration_text()
    {
        var statement = ParseValid(SpecExamples.AttemptedStatement);
        Assert.Equal("PT1234S", statement.Result!.Duration!.Value.ToString());
        Assert.Equal(TimeSpan.FromSeconds(1234), statement.Result.Duration.Value.ToTimeSpan());
        Assert.Equal(0.95, statement.Result.Score!.Scaled);
        Assert.Equal(new DateTimeOffset(2015, 12, 18, 12, 17, 0, TimeSpan.Zero), statement.Timestamp);
    }

    [Fact]
    public void Long_example_reads_every_part()
    {
        var statement = ParseValid(SpecExamples.LongStatement);
        var team = Assert.IsType<Group>(statement.Actor);
        Assert.Equal("Team PB", team.Name);
        Assert.Equal(new Mbox("mailto:teampb@example.com"), team.Ifi);
        Assert.Equal([typeof(Account), typeof(OpenId), typeof(MboxSha1Sum)], team.Members.Select(m => m.Ifi.GetType()));
        Assert.Equal("1.0.0", statement.Version);
        Assert.Equal("tlh", statement.Context!.Language);
        Assert.Equal(2, statement.Context.ContextActivities!.Other.Count);
        Assert.Equal(Guid.Parse("6690e6c9-3ef0-4ed3-8b37-7f3964730bee"), statement.Context.Statement!.Id);
        Assert.Equal(
            "X:\\meetings\\minutes\\examplemeeting.one",
            statement.Result!.Extensions![new Iri("http://example.com/profiles/meetings/resultextensions/minuteslocation")].GetString());
        var meeting = Assert.IsType<Activity>(statement.Object);
        Assert.Equal(new Iri("http://virtualmeeting.example.com/345256"), meeting.Definition!.MoreInfo);
        Assert.Equal("example meeting", meeting.Definition.Name!["en-GB"]);
        Assert.Equal(new DateTimeOffset(2013, 5, 18, 5, 32, 34, 804, TimeSpan.Zero), statement.Stored);
    }

    [Theory]
    [MemberData(nameof(Objects))]
    public void Example_objects_are_valid_statement_objects(string json)
    {
        var statement = ParseValid(InObject(json));
        Assert.Equal(statement, ParseValid(XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0)));
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void Example_interaction_definitions_are_valid(string definition, InteractionType type, int components)
    {
        var json = InObject("""{"id": "http://example.com/question", "definition": """ + definition + "}");
        var activity = Assert.IsType<Activity>(ParseValid(json).Object);
        Assert.Equal(type, activity.Definition!.InteractionType);
        Assert.Equal(new Iri("http://adlnet.gov/expapi/activities/cmi.interaction"), activity.Definition.Type);
        var lists = new[] { activity.Definition.Choices, activity.Definition.Scale, activity.Definition.Source, activity.Definition.Target, activity.Definition.Steps };
        Assert.Equal(components, lists.Sum(list => list?.Count ?? 0));
        var written = XapiJson.SerializeActivity(activity, XapiVersion.V2_0_0);
        Assert.Equal(activity, XapiJson.ParseActivity(written, XapiVersion.V2_0_0).GetValueOrThrow());
    }

    private static string InObject(string json) => MinimalStatement.TrimEnd().TrimEnd('}').Replace(
        "\"object\": { \"id\": \"http://example.com/activities/1\" }",
        "\"object\": " + json) + "}";
}
