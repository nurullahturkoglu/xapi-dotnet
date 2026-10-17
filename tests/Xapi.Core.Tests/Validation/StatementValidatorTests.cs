using System.Text.Json;
using Xapi.Validation;

namespace Xapi.Core.Tests.Validation;

public class StatementValidatorTests
{
    private static readonly Agent Learner = Agent.FromMbox("learner@example.com");
    private static readonly Activity Course = new(new Iri("http://example.com/activities/1"));
    private static readonly Statement Base = new(Learner, Verbs.Completed, Course);

    private static void AssertValid(Statement statement, XapiVersion? version = null) =>
        Assert.Empty(StatementValidator.Validate(statement, version ?? XapiVersion.V2_0_0));

    private static void AssertInvalid(Statement statement, string code, string path, XapiVersion? version = null)
    {
        var error = Assert.Single(StatementValidator.Validate(statement, version ?? XapiVersion.V2_0_0));
        Assert.Equal((code, path), (error.Code, error.Path));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static Statement WithDefinition(ActivityDefinition definition) => Base with { Object = Course with { Definition = definition } };

    private static Statement WithScore(Score score) => Base with { Result = new Result { Score = score } };

    [Fact]
    public void A_minimal_statement_is_valid() => AssertValid(Base);

    [Theory]
    [InlineData("1.0", true, true)]
    [InlineData("1.0.0", true, true)]
    [InlineData("1.0.3", true, true)]
    [InlineData("1.0.99", true, true)]
    [InlineData("1.0.3-rc.1+build", true, true)]
    [InlineData("2.0.0", false, true)]
    [InlineData("2.0", false, false)]
    [InlineData("2.0.1", false, false)]
    [InlineData("1.1.0", false, false)]
    [InlineData("0.95", false, false)]
    [InlineData("1.0.", false, false)]
    [InlineData("", false, false)]
    public void Statement_version_must_match_the_profile(string version, bool valid103, bool valid200)
    {
        var statement = Base with { Version = version };
        Assert.Equal(valid103, StatementValidator.Validate(statement, XapiVersion.V1_0_3).Count == 0);
        Assert.Equal(valid200, StatementValidator.Validate(statement, XapiVersion.V2_0_0).Count == 0);
        if (!valid200)
        {
            AssertInvalid(statement, XapiErrorCodes.Version, "$.version");
        }
    }

    [Theory]
    [InlineData("mailto:a@example.com", true)]
    [InlineData("mailto:first.last+tag@sub.example.co.uk", true)]
    [InlineData("a@example.com", false)]
    [InlineData("MAILTO:a@example.com", false)]
    [InlineData("mailto:", false)]
    [InlineData("mailto:example.com", false)]
    [InlineData("mailto:@example.com", false)]
    [InlineData("mailto:a@", false)]
    [InlineData("mailto:a@b@example.com", false)]
    [InlineData("mailto:a@.example.com", false)]
    [InlineData("mailto:a@example..com", false)]
    [InlineData("mailto:a@example.com.", false)]
    [InlineData("mailto:a@exa mple.com", false)]
    public void Mbox_must_be_a_mailto_iri(string mbox, bool valid)
    {
        var statement = Base with { Actor = new Agent(new Mbox(mbox)) };
        if (valid)
        {
            AssertValid(statement);
        }
        else
        {
            AssertInvalid(statement, XapiErrorCodes.Mbox, "$.actor.mbox");
        }
    }

    [Theory]
    [InlineData("ebd31e95054c018b10727ccffd2ef2ec3a016ee9", true)]
    [InlineData("EBD31E95054C018B10727CCFFD2EF2EC3A016EE9", true)]
    [InlineData("ebd31e95054c018b10727ccffd2ef2ec3a016ee", false)]
    [InlineData("ebd31e95054c018b10727ccffd2ef2ec3a016eeg", false)]
    [InlineData("", false)]
    public void Mbox_sha1sum_must_be_40_hex_digits(string value, bool valid)
    {
        var statement = Base with { Actor = new Agent(new MboxSha1Sum(value)) };
        if (valid)
        {
            AssertValid(statement);
        }
        else
        {
            AssertInvalid(statement, XapiErrorCodes.Sha1, "$.actor.mbox_sha1sum");
        }
    }

    [Fact]
    public void Identifiers_are_checked_wherever_agents_appear()
    {
        var bad = new Agent(new Mbox("nope"));
        AssertInvalid(Base with { Object = bad }, XapiErrorCodes.Mbox, "$.object.mbox");
        AssertInvalid(Base with { Context = new Context { Instructor = bad } }, XapiErrorCodes.Mbox, "$.context.instructor.mbox");
        AssertInvalid(Base with { Actor = new Group([Learner, bad]) }, XapiErrorCodes.Mbox, "$.actor.member[1].mbox");
        AssertInvalid(Base with { Context = new Context { ContextAgents = [new ContextAgent(bad)] } }, XapiErrorCodes.Mbox, "$.context.contextAgents[0].agent.mbox");
    }

    [Fact]
    public void Anonymous_groups_need_members()
    {
        AssertInvalid(Base with { Actor = new Group(XapiList<Agent>.Empty) }, XapiErrorCodes.GroupMemberMissing, "$.actor.member");
        AssertValid(Base with { Actor = new Group([Learner]) });
        AssertValid(Base with { Actor = new Group(new Mbox("mailto:team@example.com")) });
    }

    [Fact]
    public void Voiding_statements_must_target_a_statement_ref()
    {
        AssertInvalid(Base with { Verb = Verbs.Voided }, XapiErrorCodes.VoidingObject, "$.object");
        AssertValid(Base with { Verb = Verbs.Voided, Object = new StatementRef(Guid.NewGuid()) });
        AssertValid(Base with { Verb = new Verb(new Iri("http://adlnet.gov/expapi/verbs/Voided")) });
    }

    [Fact]
    public void SubStatements_cannot_contain_substatements()
    {
        var inner = new SubStatement(Learner, Verbs.Attempted, Course);
        AssertValid(Base with { Object = inner });
        AssertInvalid(Base with { Object = inner with { Object = inner } }, XapiErrorCodes.SubStatementNested, "$.object.object");
    }

    [Fact]
    public void SubStatement_contents_are_validated()
    {
        var inner = new SubStatement(new Agent(new Mbox("bad")), Verbs.Attempted, Course) { Result = new Result { Score = new Score { Scaled = 2 } } };
        var errors = StatementValidator.Validate(Base with { Object = inner }, XapiVersion.V2_0_0);
        Assert.Equal(["$.object.actor.mbox", "$.object.result.score.scaled"], errors.Select(e => e.Path));
    }

    [Fact]
    public void Group_authority_must_be_an_anonymous_pair()
    {
        var other = Agent.FromMbox("client@example.com");
        AssertValid(Base with { Authority = new Group([Learner, other]) });
        AssertValid(Base with { Authority = other });
        AssertInvalid(Base with { Authority = new Group([Learner]) }, XapiErrorCodes.AuthorityGroup, "$.authority");
        AssertInvalid(Base with { Authority = new Group([Learner, other, Agent.FromMbox("x@example.com")]) }, XapiErrorCodes.AuthorityGroup, "$.authority");
        AssertInvalid(Base with { Authority = new Group(new Mbox("mailto:g@example.com")) { Members = [Learner, other] } }, XapiErrorCodes.AuthorityGroup, "$.authority");
    }

    [Theory]
    [InlineData(-1.0, true)]
    [InlineData(0.0, true)]
    [InlineData(1.0, true)]
    [InlineData(1.0001, false)]
    [InlineData(-1.5, false)]
    public void Scaled_score_is_between_minus_one_and_one(double scaled, bool valid)
    {
        if (valid)
        {
            AssertValid(WithScore(new Score { Scaled = scaled }));
        }
        else
        {
            AssertInvalid(WithScore(new Score { Scaled = scaled }), XapiErrorCodes.ScoreScaled, "$.result.score.scaled");
        }
    }

    [Theory]
    [InlineData(5.0, 0.0, 10.0, true)]
    [InlineData(0.0, 0.0, 10.0, true)]
    [InlineData(10.0, 0.0, 10.0, true)]
    [InlineData(-1.0, 0.0, 10.0, false)]
    [InlineData(11.0, 0.0, 10.0, false)]
    [InlineData(11.0, null, 10.0, false)]
    [InlineData(-1.0, 0.0, null, false)]
    [InlineData(1000.0, null, null, true)]
    public void Raw_score_is_between_min_and_max(double raw, double? min, double? max, bool valid)
    {
        var statement = WithScore(new Score { Raw = raw, Min = min, Max = max });
        if (valid)
        {
            AssertValid(statement);
        }
        else
        {
            AssertInvalid(statement, XapiErrorCodes.ScoreRaw, "$.result.score.raw");
        }
    }

    [Fact]
    public void Min_must_be_less_than_max()
    {
        AssertInvalid(WithScore(new Score { Min = 10, Max = 0 }), XapiErrorCodes.ScoreMinMax, "$.result.score.min");
        AssertInvalid(WithScore(new Score { Min = 5, Max = 5 }), XapiErrorCodes.ScoreMinMax, "$.result.score.min");
        AssertValid(WithScore(new Score { Min = 4, Max = 5 }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Scores_must_be_finite(double value) =>
        AssertInvalid(WithScore(new Score { Max = value }), XapiErrorCodes.ScoreNotFinite, "$.result.score.max");

    [Fact]
    public void Revision_and_platform_need_an_activity_object()
    {
        var context = new Context { Revision = "r1", Platform = "LMS" };
        AssertValid(Base with { Context = context });
        var errors = StatementValidator.Validate(Base with { Object = Learner, Context = context }, XapiVersion.V1_0_3);
        Assert.Equal([XapiErrorCodes.ContextRevision, XapiErrorCodes.ContextPlatform], errors.Select(e => e.Code));
        AssertInvalid(Base with { Object = new StatementRef(Guid.NewGuid()), Context = new Context { Platform = "LMS" } }, XapiErrorCodes.ContextPlatform, "$.context.platform");
    }

    [Fact]
    public void Context_language_must_be_a_language_tag()
    {
        AssertValid(Base with { Context = new Context { Language = "zh-Hant-TW" } });
        AssertInvalid(Base with { Context = new Context { Language = "en_US" } }, XapiErrorCodes.LanguageTag, "$.context.language");
    }

    [Fact]
    public void Context_agents_and_groups_are_2_0_only()
    {
        var statement = Base with
        {
            Context = new Context
            {
                ContextAgents = [new ContextAgent(Learner)],
                ContextGroups = [new ContextGroup(new Group([Learner]))],
            },
        };
        AssertValid(statement);
        var errors = StatementValidator.Validate(statement, XapiVersion.V1_0_3);
        Assert.Equal(["$.context.contextAgents", "$.context.contextGroups"], errors.Select(e => e.Path));
        Assert.All(errors, e => Assert.Equal(XapiErrorCodes.ContextVersion, e.Code));
    }

    [Fact]
    public void Context_groups_must_be_valid_groups() =>
        AssertInvalid(
            Base with { Context = new Context { ContextGroups = [new ContextGroup(new Group(XapiList<Agent>.Empty))] } },
            XapiErrorCodes.GroupMemberMissing,
            "$.context.contextGroups[0].group.member");

    [Fact]
    public void Interaction_properties_need_an_interaction_type()
    {
        AssertInvalid(WithDefinition(new ActivityDefinition { CorrectResponsesPattern = ["a"] }), XapiErrorCodes.InteractionTypeMissing, "$.object.definition.interactionType");
        AssertInvalid(WithDefinition(new ActivityDefinition { Choices = [new InteractionComponent("a")] }), XapiErrorCodes.InteractionTypeMissing, "$.object.definition.interactionType");
        AssertValid(WithDefinition(new ActivityDefinition { InteractionType = InteractionType.Choice, Choices = [new InteractionComponent("a")] }));
        AssertValid(WithDefinition(new ActivityDefinition { Name = LanguageMap.Create("en", "x") }));
    }

    [Fact]
    public void Undefined_interaction_types_are_rejected() =>
        AssertInvalid(WithDefinition(new ActivityDefinition { InteractionType = (InteractionType)99 }), XapiErrorCodes.InteractionType, "$.object.definition.interactionType");

    [Theory]
    [InlineData(InteractionType.Choice, "choices", true)]
    [InlineData(InteractionType.Sequencing, "choices", true)]
    [InlineData(InteractionType.Likert, "scale", true)]
    [InlineData(InteractionType.Matching, "source", true)]
    [InlineData(InteractionType.Matching, "target", true)]
    [InlineData(InteractionType.Performance, "steps", true)]
    [InlineData(InteractionType.TrueFalse, "choices", false)]
    [InlineData(InteractionType.Choice, "scale", false)]
    [InlineData(InteractionType.Likert, "choices", false)]
    [InlineData(InteractionType.FillIn, "steps", false)]
    [InlineData(InteractionType.Other, "source", false)]
    public void Component_lists_must_fit_the_interaction_type(InteractionType type, string list, bool valid)
    {
        XapiList<InteractionComponent> components = [new InteractionComponent("a")];
        var definition = list switch
        {
            "choices" => new ActivityDefinition { Choices = components },
            "scale" => new ActivityDefinition { Scale = components },
            "source" => new ActivityDefinition { Source = components },
            "target" => new ActivityDefinition { Target = components },
            _ => new ActivityDefinition { Steps = components },
        };
        var statement = WithDefinition(definition with { InteractionType = type });
        if (valid)
        {
            AssertValid(statement);
        }
        else
        {
            AssertInvalid(statement, XapiErrorCodes.InteractionComponentsNotAllowed, "$.object.definition." + list);
        }
    }

    [Fact]
    public void Component_ids_are_unique_within_a_list()
    {
        var duplicate = new ActivityDefinition
        {
            InteractionType = InteractionType.Matching,
            Source = [new InteractionComponent("a"), new InteractionComponent("b"), new InteractionComponent("a")],
            Target = [new InteractionComponent("a")],
        };
        AssertInvalid(WithDefinition(duplicate), XapiErrorCodes.InteractionComponentDuplicate, "$.object.definition.source[2].id");
    }

    [Fact]
    public void Definitions_in_context_activities_are_validated() =>
        AssertInvalid(
            Base with { Context = new Context { ContextActivities = new ContextActivities { Category = [Course with { Definition = new ActivityDefinition { Steps = [] } }] } } },
            XapiErrorCodes.InteractionTypeMissing,
            "$.context.contextActivities.category[0].definition.interactionType");

    [Theory]
    [InlineData("text/plain", 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", null, null)]
    [InlineData("application/vnd.example+json; charset=utf-8", 10, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", null, null)]
    [InlineData("text", 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", XapiErrorCodes.MediaType, "contentType")]
    [InlineData("text/plain", -1, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", XapiErrorCodes.AttachmentLength, "length")]
    [InlineData("text/plain", 0, "not-a-hash", XapiErrorCodes.Sha2, "sha2")]
    [InlineData("text/plain", 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85", XapiErrorCodes.Sha2, "sha2")]
    public void Attachment_rules(string contentType, long length, string sha2, string? code, string? property)
    {
        var attachment = new Attachment(new Iri("http://example.com/usage"), LanguageMap.Create("en", "file"), contentType, length, sha2);
        var statement = Base with { Attachments = [attachment] };
        if (code is null)
        {
            AssertValid(statement);
        }
        else
        {
            AssertInvalid(statement, code, "$.attachments[0]." + property);
        }
    }

    [Fact]
    public void Optional_iris_must_not_be_default()
    {
        AssertInvalid(WithDefinition(new ActivityDefinition { Type = default(Iri) }), XapiErrorCodes.Iri, "$.object.definition.type");
        AssertInvalid(Base with { Context = new Context { ContextAgents = [new ContextAgent(Learner) { RelevantTypes = [default] }] } }, XapiErrorCodes.Iri, "$.context.contextAgents[0].relevantTypes[0]");
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var statement = Base with
        {
            Actor = new Agent(new Mbox("bad")),
            Verb = Verbs.Voided,
            Result = new Result { Score = new Score { Scaled = 5 } },
            Context = new Context { Language = "!" },
        };
        Assert.Equal(4, StatementValidator.Validate(statement, XapiVersion.V2_0_0).Count);
    }

    [Fact]
    public void ValidateBatch_rejects_duplicate_ids_and_prefixes_paths()
    {
        var id = Guid.NewGuid();
        var batch = new[] { Base with { Id = id }, Base with { Id = Guid.NewGuid() }, Base with { Id = id, Actor = new Agent(new Mbox("bad")) } };
        var errors = StatementValidator.ValidateBatch(batch, XapiVersion.V2_0_0);
        Assert.Equal([("$[2].actor.mbox", XapiErrorCodes.Mbox), ("$[2].id", XapiErrorCodes.BatchDuplicateId)], errors.Select(e => (e.Path, e.Code)).OrderBy(e => e.Path, StringComparer.Ordinal));
        Assert.Empty(StatementValidator.ValidateBatch([Base, Base], XapiVersion.V2_0_0));
    }

    [Fact]
    public void Actors_activities_and_people_can_be_validated_on_their_own()
    {
        Assert.Equal(XapiErrorCodes.Mbox, Assert.Single(StatementValidator.ValidateActor(new Agent(new Mbox("bad")), XapiVersion.V2_0_0)).Code);
        Assert.Equal("$.mbox", Assert.Single(StatementValidator.ValidateActor(new Agent(new Mbox("bad")), XapiVersion.V2_0_0)).Path);
        Assert.Empty(StatementValidator.ValidateActivity(Course, XapiVersion.V2_0_0));
        Assert.Single(StatementValidator.ValidatePerson(new Person { Mboxes = [new Mbox("bad")] }, XapiVersion.V2_0_0));
    }

    [Fact]
    public void Extensions_are_not_validated()
    {
        using var document = JsonDocument.Parse("""{"anything": [null, "goes"]}""");
        AssertValid(Base with { Result = new Result { Extensions = Extensions.Create(new Iri("http://example.com/x"), document.RootElement) } });
    }
}
