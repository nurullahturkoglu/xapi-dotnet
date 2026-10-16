namespace Xapi.Core.Tests.Model;

public class ModelTests
{
    private static readonly Activity Course = new(new Iri("http://example.com/activities/1"));

    [Fact]
    public void Agent_factories_build_identified_agents()
    {
        Assert.Equal(new Agent(new Mbox("mailto:a@example.com")) { Name = "A" }, Agent.FromMbox("a@example.com", "A"));
        var account = Agent.FromAccount(new Uri("http://www.example.com"), "1625378", "Learner");
        Assert.Equal(new Account(new Iri("http://www.example.com"), "1625378"), account.Ifi);
        Assert.Equal("Learner", account.Name);
    }

    [Fact]
    public void Mbox_FromEmail_adds_the_scheme_once()
    {
        Assert.Equal("mailto:a@example.com", Mbox.FromEmail("a@example.com").Value);
        Assert.Equal("mailto:a@example.com", Mbox.FromEmail("mailto:a@example.com").Value);
    }

    [Fact]
    public void Required_members_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => new Statement(null!, Verbs.Completed, Course));
        Assert.Throws<ArgumentNullException>(() => new Statement(Agent.FromMbox("a@example.com"), null!, Course));
        Assert.Throws<ArgumentNullException>(() => new Statement(Agent.FromMbox("a@example.com"), Verbs.Completed, null!));
        Assert.Throws<ArgumentNullException>(() => new Mbox(null!));
        Assert.Throws<ArgumentNullException>(() => new Agent(null!));
        Assert.Throws<ArgumentNullException>(() => new Statement(Agent.FromMbox("a@example.com"), Verbs.Completed, Course) with { Actor = null! });
    }

    [Fact]
    public void Group_members_default_to_empty()
    {
        var group = new Group(new Mbox("mailto:g@example.com"));
        Assert.Empty(group.Members);
        Assert.False(group.IsAnonymous);
        Assert.True(new Group([Agent.FromMbox("a@example.com")]).IsAnonymous);
    }

    [Fact]
    public void Records_compare_by_value()
    {
        var a = new Statement(Agent.FromMbox("a@example.com"), Verbs.Completed, Course) { Result = new Result { Score = new Score { Raw = 1 } } };
        var b = new Statement(Agent.FromMbox("a@example.com"), Verbs.Completed, new Activity(new Iri("http://example.com/activities/1"))) { Result = new Result { Score = new Score { Raw = 1 } } };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Version = "2.0.0" });
    }

    [Fact]
    public void Absent_and_empty_correct_responses_differ() =>
        Assert.NotEqual(new ActivityDefinition(), new ActivityDefinition { CorrectResponsesPattern = [] });

    [Fact]
    public void Verbs_use_adl_ids_with_an_english_display()
    {
        Assert.Equal("http://adlnet.gov/expapi/verbs/voided", Verbs.VoidedId.Value);
        Assert.Equal(Verbs.VoidedId, Verbs.Voided.Id);
        foreach (var verb in new[] { Verbs.Completed, Verbs.Passed, Verbs.Failed, Verbs.Attempted, Verbs.Experienced, Verbs.Answered, Verbs.Initialized, Verbs.Terminated })
        {
            var name = verb.Id.Value["http://adlnet.gov/expapi/verbs/".Length..];
            Assert.Equal(name, verb.Display!["en-US"]);
        }
    }
}
