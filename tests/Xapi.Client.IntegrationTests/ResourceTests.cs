namespace Xapi.Client.IntegrationTests;

public class ResourceTests(LrsqlFixture lrs)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Person_combines_what_the_lrs_knows_about_an_agent(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var agent = Data.NewAgent();
        await client.SaveStatementAsync(Data.NewStatement(agent), cancellationToken: Ct);

        var person = await client.GetPersonAsync(agent, Ct);

        Assert.Contains(agent.Ifi, person.Mboxes.Cast<InverseFunctionalIdentifier>());
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Activity_definition_is_returned_once_a_statement_uses_it(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var activityId = Data.NewActivityId();
        await client.SaveStatementAsync(Data.NewStatement(activityId: activityId), cancellationToken: Ct);

        var activity = await client.GetActivityAsync(activityId, Ct);

        Assert.NotNull(activity);
        Assert.Equal(activityId, activity.Id);
        Assert.Equal("Integration course", activity.Definition?.Name?["en-US"]);
    }
}
