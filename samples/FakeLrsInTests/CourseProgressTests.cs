using Xapi;
using Xapi.Client;

namespace FakeLrsInTests;

public class CourseProgressTests(FakeLrs lrs) : IClassFixture<FakeLrs>
{
    private static readonly Iri Course = new("https://courses.example.com/fire-safety");

    // The tests of this class share one LRS, so each uses a learner of its own
    private static Agent NewLearner() => Agent.FromMbox($"{Guid.NewGuid():N}@example.com");

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("2.0.0")]
    public async Task Completing_a_course_stores_a_completed_statement_with_the_score(string version)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.True(XapiVersion.TryParseHeader(version, out var xapiVersion));
        var client = lrs.CreateClient(xapiVersion);
        var learner = NewLearner();

        var id = await new CourseProgress(client).CompleteAsync(learner, Course, 0.9, ct);

        var result = await client.QueryStatementsAsync(new StatementQuery { Agent = learner, Verb = Verbs.Completed.Id }, ct);
        var statement = Assert.Single(result.Statements);
        Assert.Equal(id, statement.Id);
        Assert.Equal(Course, Assert.IsType<Activity>(statement.Object).Id);
        Assert.Equal(0.9, statement.Result?.Score?.Scaled);
    }

    [Fact]
    public async Task A_new_bookmark_replaces_the_previous_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = lrs.CreateClient();
        var progress = new CourseProgress(client);
        var learner = NewLearner();

        await progress.SaveBookmarkAsync(learner, Course, "module-2/page-3", ct);
        await progress.SaveBookmarkAsync(learner, Course, "module-3/page-1", ct);

        Assert.Equal("module-3/page-1", await progress.GetBookmarkAsync(learner, Course, ct));
        Assert.Equal(["bookmark"], await client.GetStateIdsAsync(Course, learner, cancellationToken: ct));
    }

    [Fact]
    public async Task There_is_no_bookmark_before_the_first_one_is_saved()
    {
        var progress = new CourseProgress(lrs.CreateClient());

        Assert.Null(await progress.GetBookmarkAsync(NewLearner(), Course, TestContext.Current.CancellationToken));
    }
}
