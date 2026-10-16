namespace Xapi.Client.Tests.Fakes;

internal static class TestClient
{
    public const string Endpoint = "https://lrs.example.com/xapi/";

    public static (XapiClient Client, FakeHandler Handler) Create(XapiVersion? version = null, Action<XapiClientOptions>? configure = null)
    {
        var handler = new FakeHandler();
        var options = new XapiClientOptions
        {
            Endpoint = new Uri(Endpoint),
            Version = version ?? XapiVersion.V2_0_0,
            Credentials = XapiCredentials.Basic("key", "secret"),
        };
        configure?.Invoke(options);
        return (new XapiClient(new HttpClient(handler), options), handler);
    }
}
