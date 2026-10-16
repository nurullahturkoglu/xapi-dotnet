namespace Xapi.Client.IntegrationTests;

public class AboutTests(LrsqlFixture lrs)
{
    [Fact]
    public async Task About_lists_both_versions()
    {
        var about = await lrs.CreateClient(null).GetAboutAsync(TestContext.Current.CancellationToken);

        Assert.Contains(about.Version, v => v.StartsWith("2.0", StringComparison.Ordinal));
        Assert.Contains(about.Version, v => v.StartsWith("1.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Negotiation_picks_2_0_0()
    {
        var version = await lrs.CreateClient(null).GetVersionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(XapiVersion.V2_0_0, version);
    }
}
