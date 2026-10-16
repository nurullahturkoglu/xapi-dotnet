using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xapi.Json;
using static Xapi.Core.Tests.TestData.Json;

namespace Xapi.Core.Tests.Json;

public class StatementReaderTests
{
    [Fact]
    public void Reads_the_minimal_statement()
    {
        var statement = Valid();
        var agent = Assert.IsType<Agent>(statement.Actor);
        Assert.Equal(new Mbox("mailto:learner@example.com"), agent.Ifi);
        Assert.Equal(Verbs.Completed.Id, statement.Verb.Id);
        Assert.Equal(new Iri("http://example.com/activities/1"), Assert.IsType<Activity>(statement.Object).Id);
        Assert.Null(statement.Id);
        Assert.Null(statement.Timestamp);
    }

    [Fact]
    public void All_overloads_read_the_same_statement()
    {
        var fromString = XapiJson.ParseStatement(MinimalStatement, XapiVersion.V2_0_0).GetValueOrThrow();
        var fromBytes = XapiJson.ParseStatement(Encoding.UTF8.GetBytes(MinimalStatement), XapiVersion.V2_0_0).GetValueOrThrow();
        using var document = JsonDocument.Parse(MinimalStatement);
        var fromElement = XapiJson.ParseStatement(document.RootElement, XapiVersion.V2_0_0).GetValueOrThrow();
        Assert.Equal(fromString, fromBytes);
        Assert.Equal(fromString, fromElement);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"actor": }""")]
    [InlineData("""{} {}""")]
    [InlineData("""{"a":1,}""")]
    [InlineData("// comment\n{}")]
    public void Malformed_json_is_a_syntax_error_at_the_root(string json) =>
        AssertSingleError(Parse(json), XapiErrorCodes.JsonSyntax, "$");

    [Theory]
    [InlineData("[]")]
    [InlineData("\"statement\"")]
    [InlineData("42")]
    public void A_statement_must_be_an_object(string json) =>
        AssertSingleError(Parse(json), XapiErrorCodes.JsonType, "$");

    [Theory]
    [InlineData("actor")]
    [InlineData("verb")]
    [InlineData("object")]
    public void Required_properties_are_reported_where_they_belong(string property) =>
        AssertSingleError(Parse(Minimal(s => s.Remove(property))), XapiErrorCodes.PropertyRequired, "$." + property);

    [Fact]
    public void Missing_verb_id_is_reported_on_the_verb() =>
        AssertSingleError(Parse(Minimal(s => s["verb"] = new JsonObject())), XapiErrorCodes.PropertyRequired, "$.verb.id");

    [Fact]
    public void Every_error_is_collected()
    {
        var result = Parse(Minimal(s =>
        {
            s["id"] = "not-a-uuid";
            s["verb"]!["id"] = "not an iri";
            s["timestamp"] = "yesterday";
            s["foo"] = 1;
        }));
        Assert.False(result.IsSuccess);
        Assert.Equal(
            ["$.foo json.property.unknown", "$.id format.uuid", "$.timestamp format.timestamp", "$.verb.id format.iri"],
            result.Errors.Select(e => $"{e.Path} {e.Code}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GetValueOrThrow_lists_the_errors()
    {
        var result = Parse(Minimal(s => s["foo"] = 1));
        var exception = Assert.Throws<XapiValidationException>(() => result.GetValueOrThrow());
        Assert.Equal(XapiErrorCodes.PropertyUnknown, Assert.Single(exception.Errors).Code);
        Assert.Contains("$.foo", exception.Message, StringComparison.Ordinal);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Unknown_properties_are_rejected_in_strict_mode_and_ignored_in_lenient_mode()
    {
        var json = Minimal(s => s["actor"]!["shoeSize"] = 42);
        AssertSingleError(Parse(json), XapiErrorCodes.PropertyUnknown, "$.actor.shoeSize");
        Assert.True(Parse(json, mode: XapiReadMode.Lenient).IsSuccess);
    }

    [Fact]
    public void Wrongly_cased_property_names_are_reported_as_such()
    {
        var json = Minimal(s => { s["Actor"] = s["actor"]!.DeepClone(); s.Remove("actor"); });
        var result = Parse(json);
        AssertHasError(result, XapiErrorCodes.PropertyCase, "$.Actor");
        AssertHasError(result, XapiErrorCodes.PropertyRequired, "$.actor");
    }

    [Fact]
    public void Duplicate_properties_are_rejected_in_strict_mode()
    {
        const string Json = """
            {"actor":{"mbox":"mailto:a@example.com"},"verb":{"id":"http://example.com/v"},
             "object":{"id":"http://example.com/a"},"object":{"id":"http://example.com/b"}}
            """;
        AssertSingleError(Parse(Json), XapiErrorCodes.PropertyDuplicate, "$.object");
        var lenient = Parse(Json, mode: XapiReadMode.Lenient);
        Assert.Equal(new Iri("http://example.com/a"), Assert.IsType<Activity>(lenient.Value!.Object).Id);
    }

    [Fact]
    public void Null_values_are_rejected_in_strict_mode_and_treated_as_absent_in_lenient_mode()
    {
        var json = Minimal(s => s["result"] = null);
        AssertSingleError(Parse(json), XapiErrorCodes.JsonNull, "$.result");
        Assert.Null(Parse(json, mode: XapiReadMode.Lenient).Value!.Result);
    }

    [Fact]
    public void Null_is_kept_inside_extensions()
    {
        var statement = ParseValid(Minimal(s => s["result"] = new JsonObject { ["extensions"] = new JsonObject { ["http://example.com/x"] = null } }));
        Assert.Equal(JsonValueKind.Null, statement.Result!.Extensions![new Iri("http://example.com/x")].ValueKind);
    }

    [Theory]
    [InlineData("""{"id": 1}""", "$.verb.id")]
    [InlineData("""{"id": "http://example.com/v", "display": "completed"}""", "$.verb.display")]
    [InlineData("""{"id": "http://example.com/v", "display": {"en-US": 1}}""", "$.verb.display['en-US']")]
    [InlineData("""[]""", "$.verb")]
    public void Wrong_json_types_are_reported_with_their_path(string verb, string path) =>
        AssertSingleError(Parse(Minimal(s => s["verb"] = JsonNode.Parse(verb))), XapiErrorCodes.JsonType, path);

    [Fact]
    public void Language_map_keys_must_be_language_tags()
    {
        var json = Minimal(s => s["verb"]!["display"] = new JsonObject { ["en-US"] = "completed", ["not a tag"] = "x" });
        AssertSingleError(Parse(json), XapiErrorCodes.LanguageTag, "$.verb.display['not a tag']");
        var lenient = Parse(json, mode: XapiReadMode.Lenient).GetValueOrThrow();
        Assert.Equal("completed", Assert.Single(lenient.Verb.Display!).Value);
    }

    [Fact]
    public void Language_map_keys_differing_only_in_case_are_duplicates()
    {
        var json = Minimal(s => s["verb"]!["display"] = new JsonObject { ["en-US"] = "a", ["en-us"] = "b" });
        AssertSingleError(Parse(json), XapiErrorCodes.PropertyDuplicate, "$.verb.display['en-us']");
    }

    [Fact]
    public void Extension_keys_must_be_iris()
    {
        var json = Minimal(s => s["result"] = new JsonObject { ["extensions"] = new JsonObject { ["not-an-iri"] = 1 } });
        AssertSingleError(Parse(json), XapiErrorCodes.Iri, "$.result.extensions['not-an-iri']");
    }

    [Fact]
    public void Duplicate_properties_inside_extension_values_are_rejected_in_strict_mode()
    {
        var json = MinimalStatement.TrimEnd().TrimEnd('}') + """, "result": {"extensions": {"http://example.com/x": {"a": 1, "a": 2}}}}""";
        AssertSingleError(Parse(json), XapiErrorCodes.PropertyDuplicate, "$.result.extensions['http://example.com/x'].a");
    }

    [Fact]
    public void Extension_values_are_preserved_exactly()
    {
        const string Value = """{"n":1.50,"s":"\u00e9","a":[true,null]}""";
        var json = MinimalStatement.TrimEnd().TrimEnd('}') + """, "context": {"extensions": {"http://example.com/x": """ + Value + "}}}";
        Assert.Equal(Value, ParseValid(json).Context!.Extensions![new Iri("http://example.com/x")].GetRawText());
    }

    [Theory]
    [InlineData("""{"mbox": "mailto:a@example.com", "openid": "http://example.com/a"}""", XapiErrorCodes.IfiMultiple)]
    [InlineData("""{"mbox": "mailto:a@example.com", "account": {"homePage": "http://example.com", "name": "a"}}""", XapiErrorCodes.IfiMultiple)]
    [InlineData("""{"name": "nobody"}""", XapiErrorCodes.IfiMissing)]
    [InlineData("""{"objectType": "Agent"}""", XapiErrorCodes.IfiMissing)]
    public void Agents_need_exactly_one_identifier(string actor, string code) =>
        AssertSingleError(Parse(Minimal(s => s["actor"] = JsonNode.Parse(actor))), code, "$.actor");

    [Theory]
    [InlineData("""{"mbox_sha1sum": "ebd31e95054c018b10727ccffd2ef2ec3a016ee9"}""", typeof(MboxSha1Sum))]
    [InlineData("""{"openid": "http://openid.example.org/12345"}""", typeof(OpenId))]
    [InlineData("""{"account": {"homePage": "http://www.example.com", "name": "1625378"}}""", typeof(Account))]
    [InlineData("""{"objectType": "Agent", "mbox": "mailto:a@example.com", "name": "A"}""", typeof(Mbox))]
    public void Reads_every_identifier_kind(string actor, Type ifiType)
    {
        var agent = Assert.IsType<Agent>(ParseValid(Minimal(s => s["actor"] = JsonNode.Parse(actor))).Actor);
        Assert.IsType(ifiType, agent.Ifi);
    }

    [Theory]
    [InlineData("""{"objectType": "agent", "mbox": "mailto:a@example.com"}""", XapiErrorCodes.ObjectType)]
    [InlineData("""{"objectType": "Activity", "id": "http://example.com/a"}""", XapiErrorCodes.ObjectType)]
    [InlineData("""{"objectType": 1, "mbox": "mailto:a@example.com"}""", XapiErrorCodes.JsonType)]
    public void Actor_object_type_must_be_agent_or_group(string actor, string code) =>
        AssertHasError(Parse(Minimal(s => s["actor"] = JsonNode.Parse(actor))), code, "$.actor.objectType");

    [Fact]
    public void Group_members_must_be_agents()
    {
        var json = Minimal(s => s["actor"] = JsonNode.Parse("""
            {"objectType": "Group", "member": [{"mbox": "mailto:a@example.com"}, {"objectType": "Group", "member": []}]}
            """));
        AssertSingleError(Parse(json), XapiErrorCodes.GroupMemberNotAgent, "$.actor.member[1]");
    }

    [Fact]
    public void Identified_groups_may_have_members()
    {
        var group = Assert.IsType<Group>(ParseValid(Minimal(s => s["actor"] = JsonNode.Parse("""
            {"objectType": "Group", "name": "Team", "mbox": "mailto:team@example.com", "member": [{"mbox": "mailto:a@example.com"}]}
            """))).Actor);
        Assert.False(group.IsAnonymous);
        Assert.Single(group.Members);
    }

    [Fact]
    public void Group_with_two_identifiers_is_rejected() =>
        AssertSingleError(
            Parse(Minimal(s => s["actor"] = JsonNode.Parse("""{"objectType": "Group", "mbox": "mailto:a@example.com", "openid": "http://example.com/g"}"""))),
            XapiErrorCodes.IfiMultiple,
            "$.actor");

    [Theory]
    [InlineData("""{"objectType": "StatementRef", "id": "8f87ccde-bb56-4c2e-ab83-44982ef22df0"}""", typeof(StatementRef))]
    [InlineData("""{"objectType": "Agent", "mbox": "mailto:b@example.com"}""", typeof(Agent))]
    [InlineData("""{"objectType": "Group", "member": [{"mbox": "mailto:b@example.com"}]}""", typeof(Group))]
    [InlineData("""{"objectType": "Activity", "id": "http://example.com/a"}""", typeof(Activity))]
    [InlineData("""{"objectType": "SubStatement", "actor": {"mbox": "mailto:b@example.com"}, "verb": {"id": "http://example.com/v"}, "object": {"id": "http://example.com/a"}}""", typeof(SubStatement))]
    public void Object_type_selects_the_object_kind(string @object, Type type) =>
        Assert.IsType(type, ParseValid(Minimal(s => s["object"] = JsonNode.Parse(@object))).Object);

    [Theory]
    [InlineData("""{"objectType": "Statement", "id": "8f87ccde-bb56-4c2e-ab83-44982ef22df0"}""")]
    [InlineData("""{"objectType": "activity", "id": "http://example.com/a"}""")]
    public void Unknown_object_types_are_rejected(string @object) =>
        AssertSingleError(Parse(Minimal(s => s["object"] = JsonNode.Parse(@object))), XapiErrorCodes.ObjectType, "$.object.objectType");

    [Fact]
    public void StatementRef_requires_its_object_type()
    {
        var json = Minimal(s => s["object"] = JsonNode.Parse("""{"id": "8f87ccde-bb56-4c2e-ab83-44982ef22df0"}"""));
        AssertSingleError(Parse(json), XapiErrorCodes.Iri, "$.object.id");
    }

    [Fact]
    public void SubStatements_cannot_be_nested()
    {
        var json = Minimal(s => s["object"] = JsonNode.Parse("""
            {"objectType": "SubStatement", "actor": {"mbox": "mailto:b@example.com"}, "verb": {"id": "http://example.com/v"},
             "object": {"objectType": "SubStatement", "actor": {"mbox": "mailto:b@example.com"}, "verb": {"id": "http://example.com/v"}, "object": {"id": "http://example.com/a"}}}
            """));
        AssertSingleError(Parse(json), XapiErrorCodes.SubStatementNested, "$.object.object");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("stored")]
    [InlineData("version")]
    [InlineData("authority")]
    public void SubStatements_reject_statement_only_properties(string property)
    {
        var json = Minimal(s => s["object"] = JsonNode.Parse("""
            {"objectType": "SubStatement", "actor": {"mbox": "mailto:b@example.com"}, "verb": {"id": "http://example.com/v"}, "object": {"id": "http://example.com/a"}}
            """));
        json["object"]![property] = property == "authority" ? JsonNode.Parse("""{"mbox": "mailto:c@example.com"}""") : "x";
        AssertSingleError(Parse(json), XapiErrorCodes.PropertyUnknown, "$.object." + property);
    }

    [Fact]
    public void Context_activities_accept_a_single_object_or_an_array()
    {
        var statement = ParseValid(Minimal(s => s["context"] = JsonNode.Parse("""
            {"contextActivities": {"parent": {"id": "http://example.com/p"}, "grouping": [{"id": "http://example.com/g1"}, {"id": "http://example.com/g2"}]}}
            """)));
        var activities = statement.Context!.ContextActivities!;
        Assert.Equal(new Iri("http://example.com/p"), Assert.Single(activities.Parent).Id);
        Assert.Equal(2, activities.Grouping.Count);
        Assert.Empty(activities.Category);
    }

    [Fact]
    public void Context_agents_are_rejected_under_1_0_3_and_accepted_under_2_0()
    {
        var json = Minimal(s => s["context"] = JsonNode.Parse("""
            {"contextAgents": [{"objectType": "contextAgent", "agent": {"mbox": "mailto:b@example.com"}, "relevantTypes": ["http://example.com/t"]}]}
            """));
        AssertSingleError(Parse(json, XapiVersion.V1_0_3), XapiErrorCodes.PropertyVersion, "$.context.contextAgents");
        Assert.True(Parse(json, XapiVersion.V1_0_3, XapiReadMode.Lenient).IsSuccess);
        var agent = Assert.Single(ParseValid(json).Context!.ContextAgents!);
        Assert.Equal(new Iri("http://example.com/t"), Assert.Single(agent.RelevantTypes));
    }

    [Theory]
    [InlineData("""{"contextAgents": [{"objectType": "contextAgent", "agent": {"mbox": "mailto:b@example.com"}, "relevantTypes": []}]}""", XapiErrorCodes.RelevantTypesEmpty, "$.context.contextAgents[0].relevantTypes")]
    [InlineData("""{"contextGroups": [{"objectType": "contextGroup", "group": {"objectType": "Group", "member": [{"mbox": "mailto:b@example.com"}]}, "relevantTypes": []}]}""", XapiErrorCodes.RelevantTypesEmpty, "$.context.contextGroups[0].relevantTypes")]
    [InlineData("""{"contextAgents": [{"agent": {"mbox": "mailto:b@example.com"}}]}""", XapiErrorCodes.PropertyRequired, "$.context.contextAgents[0].objectType")]
    [InlineData("""{"contextGroups": [{"objectType": "contextGroup", "group": {"mbox": "mailto:b@example.com"}}]}""", XapiErrorCodes.ObjectType, "$.context.contextGroups[0].group.objectType")]
    public void Context_agent_and_group_rules(string context, string code, string path) =>
        AssertHasError(Parse(Minimal(s => s["context"] = JsonNode.Parse(context))), code, path);

    [Fact]
    public void Statement_version_is_checked_against_the_profile()
    {
        AssertSingleError(Parse(Minimal(s => s["version"] = "2.0.0"), XapiVersion.V1_0_3), XapiErrorCodes.Version, "$.version");
        Assert.True(Parse(Minimal(s => s["version"] = "1.0.3"), XapiVersion.V2_0_0).IsSuccess);
        Assert.True(Parse(Minimal(s => s["version"] = "2.0.0"), XapiVersion.V2_0_0).IsSuccess);
    }

    [Fact]
    public void Lenient_mode_skips_semantic_validation()
    {
        var json = Minimal(s => s["result"] = JsonNode.Parse("""{"score": {"scaled": 7}}"""));
        AssertSingleError(Parse(json), XapiErrorCodes.ScoreScaled, "$.result.score.scaled");
        Assert.Equal(7, Parse(json, mode: XapiReadMode.Lenient).Value!.Result!.Score!.Scaled);
    }

    [Theory]
    [InlineData("""{"score": {"raw": 1e400}}""", "$.result.score.raw")]
    [InlineData("""{"duration": "PT1H", "success": "true"}""", "$.result.success")]
    public void Result_values_must_have_the_right_type(string result, string path) =>
        AssertSingleError(Parse(Minimal(s => s["result"] = JsonNode.Parse(result))), XapiErrorCodes.JsonType, path);

    [Fact]
    public void Timestamps_are_read_as_utc()
    {
        var statement = ParseValid(Minimal(s => s["timestamp"] = "2026-10-16T15:00:00.123+03:00"));
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 12, 0, 0, 123, TimeSpan.Zero), statement.Timestamp);
        Assert.Equal(TimeSpan.Zero, statement.Timestamp!.Value.Offset);
    }

    [Fact]
    public void Statement_arrays_report_paths_per_item()
    {
        var array = new JsonArray(Minimal(), Minimal(s => s["actor"] = new JsonObject()));
        AssertSingleError(XapiJson.ParseStatements(array.ToJsonString(), XapiVersion.V2_0_0), XapiErrorCodes.IfiMissing, "$[1].actor");
    }

    [Fact]
    public void ParseStatements_accepts_an_object_or_an_array()
    {
        Assert.Single(XapiJson.ParseStatements(MinimalStatement, XapiVersion.V2_0_0).GetValueOrThrow());
        Assert.Equal(2, XapiJson.ParseStatements(new JsonArray(Minimal(), Minimal()).ToJsonString(), XapiVersion.V2_0_0).GetValueOrThrow().Count);
        Assert.Empty(XapiJson.ParseStatements("[]", XapiVersion.V2_0_0).GetValueOrThrow());
        AssertSingleError(XapiJson.ParseStatements("1", XapiVersion.V2_0_0), XapiErrorCodes.JsonType, "$");
    }

    [Fact]
    public void ParseStatements_rejects_duplicate_ids_in_strict_mode()
    {
        const string Id = "8f87ccde-bb56-4c2e-ab83-44982ef22df0";
        var array = new JsonArray(Minimal(s => s["id"] = Id), Minimal(s => s["id"] = Id)).ToJsonString();
        AssertSingleError(XapiJson.ParseStatements(array, XapiVersion.V2_0_0), XapiErrorCodes.BatchDuplicateId, "$[1].id");
        Assert.True(XapiJson.ParseStatements(array, XapiVersion.V2_0_0, XapiReadMode.Lenient).IsSuccess);
    }

    [Fact]
    public void Property_names_that_are_not_identifiers_use_bracket_paths() =>
        AssertSingleError(Parse(Minimal(s => s["my-prop"] = 1)), XapiErrorCodes.PropertyUnknown, "$['my-prop']");
}
