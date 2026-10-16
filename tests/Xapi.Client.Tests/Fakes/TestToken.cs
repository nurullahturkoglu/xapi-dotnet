namespace Xapi.Client.Tests.Fakes;

internal static class TestToken
{
    /// <summary>Cancelled when the test run is aborted or the test times out.</summary>
    public static CancellationToken Ct => TestContext.Current.CancellationToken;
}
