using System.Text.Json;
using Xapi.Validation;

namespace Xapi.Core.Tests.Validation;

public class StatementEquivalenceTests
{
    private static readonly Agent Learner = Agent.FromMbox("learner@example.com", "Learner");
    private static readonly Activity Course = new(new Iri("http://example.com/activities/1"));
    private static readonly Statement Base = new(Learner, Verbs.Completed, Course);

    private static void Same(Statement a, Statement b)
    {
        Assert.True(StatementEquivalence.AreEquivalent(a, b));
        Assert.True(StatementEquivalence.AreEquivalent(b, a));
    }

    private static void Different(Statement a, Statement b)
    {
        Assert.False(StatementEquivalence.AreEquivalent(a, b));
        Assert.False(StatementEquivalence.AreEquivalent(b, a));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Properties_set_by_the_lrs_are_ignored() =>
        Same(
            Base,
            Base with
            {
                Id = Guid.NewGuid(),
                Stored = DateTimeOffset.UtcNow,
                Authority = Agent.FromMbox("lrs@example.com"),
                Version = "1.0.3",
                Timestamp = DateTimeOffset.UtcNow,
            });

    [Fact]
    public void Attachments_are_ignored() =>
        Same(Base, Base with { Attachments = [new Attachment(new Iri("http://example.com/u"), LanguageMap.Create("en", "a"), "text/plain", 1, new string('a', 64))] });

    [Fact]
    public void Verb_display_is_ignored() =>
        Same(Base, Base with { Verb = Verbs.Completed with { Display = LanguageMap.Create("de", "abgeschlossen") } });

    [Fact]
    public void Activity_definitions_are_ignored()
    {
        var defined = Course with { Definition = new ActivityDefinition { Name = LanguageMap.Create("en", "Course") } };
        Same(Base, Base with { Object = defined });
        Same(
            Base with { Context = new Context { ContextActivities = new ContextActivities { Parent = [Course] } } },
            Base with { Context = new Context { ContextActivities = new ContextActivities { Parent = [defined] } } });
    }

    [Fact]
    public void Case_is_ignored()
    {
        Same(Base, Base with { Actor = Agent.FromMbox("LEARNER@example.com", "learner") });
        Same(Base, Base with { Verb = new Verb(new Iri("http://ADLNET.gov/expapi/verbs/COMPLETED")) });
        Same(Base, Base with { Object = new Activity(new Iri("HTTP://EXAMPLE.COM/activities/1")) });
        Same(Base with { Context = new Context { Language = "en-US", Platform = "LMS" } }, Base with { Context = new Context { Language = "EN-us", Platform = "lms" } });
        Same(Base with { Result = new Result { Response = "Yes" } }, Base with { Result = new Result { Response = "yes" } });
    }

    [Fact]
    public void Group_member_order_is_ignored_but_membership_is_not()
    {
        var a = Agent.FromMbox("a@example.com");
        var b = Agent.FromMbox("b@example.com");
        var c = Agent.FromMbox("c@example.com");
        Same(Base with { Actor = new Group([a, b]) }, Base with { Actor = new Group([b, a]) });
        Different(Base with { Actor = new Group([a, b]) }, Base with { Actor = new Group([a, c]) });
        Different(Base with { Actor = new Group([a, a]) }, Base with { Actor = new Group([a, b]) });
        Different(Base with { Actor = new Group([a]) }, Base with { Actor = new Group([a, b]) });
    }

    [Fact]
    public void Other_lists_keep_their_order()
    {
        var p1 = new Activity(new Iri("http://example.com/p1"));
        var p2 = new Activity(new Iri("http://example.com/p2"));
        Different(
            Base with { Context = new Context { ContextActivities = new ContextActivities { Parent = [p1, p2] } } },
            Base with { Context = new Context { ContextActivities = new ContextActivities { Parent = [p2, p1] } } });
    }

    [Fact]
    public void Actor_identity_and_name_matter()
    {
        Different(Base, Base with { Actor = Agent.FromMbox("other@example.com", "Learner") });
        Different(Base, Base with { Actor = Agent.FromMbox("learner@example.com", "Someone else") });
        Different(Base, Base with { Actor = new Agent(new OpenId(new Iri("http://example.com/learner"))) { Name = "Learner" } });
        Different(Base, Base with { Actor = new Group(Learner.Ifi) { Name = "Learner" } });
    }

    [Fact]
    public void Verb_and_object_matter()
    {
        Different(Base, Base with { Verb = Verbs.Attempted });
        Different(Base, Base with { Object = new Activity(new Iri("http://example.com/activities/2")) });
        Different(Base, Base with { Object = new StatementRef(Guid.NewGuid()) });
        var id = Guid.NewGuid();
        Same(Base with { Object = new StatementRef(id) }, Base with { Object = new StatementRef(id) });
    }

    [Fact]
    public void Result_values_matter()
    {
        var result = new Result { Score = new Score { Scaled = 0.5 }, Success = true, Completion = true, Duration = XapiDuration.Parse("PT1M") };
        Same(Base with { Result = result }, Base with { Result = result with { } });
        Different(Base, Base with { Result = result });
        Different(Base with { Result = result }, Base with { Result = result with { Score = new Score { Scaled = 0.6 } } });
        Different(Base with { Result = result }, Base with { Result = result with { Success = false } });
        Different(Base with { Result = result }, Base with { Result = result with { Completion = null } });
    }

    [Fact]
    public void Durations_compare_to_hundredths_of_a_second()
    {
        Same(Base with { Result = new Result { Duration = XapiDuration.Parse("PT1.234S") } }, Base with { Result = new Result { Duration = XapiDuration.Parse("PT1.23S") } });
        Different(Base with { Result = new Result { Duration = XapiDuration.Parse("PT1.23S") } }, Base with { Result = new Result { Duration = XapiDuration.Parse("PT1.24S") } });
    }

    [Fact]
    public void Context_values_matter()
    {
        var context = new Context { Registration = Guid.NewGuid(), Instructor = Agent.FromMbox("t@example.com") };
        Same(Base with { Context = context }, Base with { Context = context with { } });
        Different(Base with { Context = context }, Base with { Context = context with { Registration = Guid.NewGuid() } });
        Different(Base with { Context = context }, Base with { Context = context with { Instructor = null } });
        Different(
            Base with { Context = new Context { ContextAgents = [new ContextAgent(Learner) { RelevantTypes = [new Iri("http://example.com/t1")] }] } },
            Base with { Context = new Context { ContextAgents = [new ContextAgent(Learner) { RelevantTypes = [new Iri("http://example.com/t2")] }] } });
    }

    [Fact]
    public void Extensions_compare_deeply_ignoring_property_order_and_case()
    {
        var key = new Iri("http://example.com/ext");
        Same(
            Base with { Result = new Result { Extensions = Extensions.Create(key, Json("""{"a":1,"b":"X"}""")) } },
            Base with { Result = new Result { Extensions = Extensions.Create(new Iri("http://EXAMPLE.com/ext"), Json("""{"b":"x","a":1.0}""")) } });
        Different(
            Base with { Result = new Result { Extensions = Extensions.Create(key, Json("""{"a":1}""")) } },
            Base with { Result = new Result { Extensions = Extensions.Create(key, Json("""{"a":2}""")) } });
        Same(Base with { Context = new Context { Extensions = Extensions.Empty } }, Base with { Context = new Context() });
    }

    [Fact]
    public void SubStatement_timestamps_compare_as_instants_to_the_millisecond()
    {
        var sub = new SubStatement(Learner, Verbs.Attempted, Course) { Timestamp = new DateTimeOffset(2026, 10, 16, 15, 0, 0, 123, TimeSpan.FromHours(3)) };
        Same(Base with { Object = sub }, Base with { Object = sub with { Timestamp = new DateTimeOffset(2026, 10, 16, 12, 0, 0, 123, TimeSpan.Zero).AddTicks(4567) } });
        Different(Base with { Object = sub }, Base with { Object = sub with { Timestamp = sub.Timestamp!.Value.AddMilliseconds(1) } });
        Different(Base with { Object = sub }, Base with { Object = sub with { Timestamp = null } });
    }

    [Fact]
    public void SubStatement_contents_matter_but_their_attachments_do_not()
    {
        var sub = new SubStatement(Learner, Verbs.Attempted, Course);
        Different(Base with { Object = sub }, Base with { Object = sub with { Verb = Verbs.Passed } });
        Same(
            Base with { Object = sub },
            Base with { Object = sub with { Attachments = [new Attachment(new Iri("http://example.com/u"), LanguageMap.Create("en", "a"), "text/plain", 1, new string('a', 64))] } });
    }

    [Fact]
    public void Arguments_must_not_be_null() =>
        Assert.Throws<ArgumentNullException>(() => StatementEquivalence.AreEquivalent(Base, null!));
}
