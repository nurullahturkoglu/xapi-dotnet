using System.Reflection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(Xapi.Client.IntegrationTests.LrsqlFixture))]

namespace Xapi.Client.IntegrationTests;

/// <summary>
/// One lrsql container for the whole test run. lrsql serves xAPI 1.0.3 and 2.0.0 on the same endpoint and picks
/// the version from the request header, so both versions share it. Tests isolate themselves with fresh ids.
/// </summary>
public sealed class LrsqlFixture : IAsyncLifetime
{
    private const string Key = "integration_key";
    private const string Secret = "integration_secret";
    private const int Port = 8080;

    private readonly IContainer _container;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public LrsqlFixture()
    {
        // No LRSQL_DB_NAME: lrsql then uses a throwaway in-memory SQLite database. Setting it would need a volume
        // at /lrsql/db (see eng/conformance/run-local.sh).
        _container = new ContainerBuilder(PinnedImage())
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithEnvironment("LRSQL_API_KEY_DEFAULT", Key)
            .WithEnvironment("LRSQL_API_SECRET_DEFAULT", Secret)
            .WithEnvironment("LRSQL_ADMIN_USER_DEFAULT", "integration_admin")
            .WithEnvironment("LRSQL_ADMIN_PASS_DEFAULT", "integration_admin_pass")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(Port).ForPath("/xapi/about"),
                wait => wait.WithTimeout(TimeSpan.FromMinutes(3))))
            .Build();
    }

    public Uri Endpoint { get; private set; } = null!;

    public XapiClient CreateClient(XapiVersion? version) => new(_http, new XapiClientOptions
    {
        Endpoint = Endpoint,
        Version = version,
        Credentials = XapiCredentials.Basic(Key, Secret),
    });

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Endpoint = new UriBuilder(Uri.UriSchemeHttp, _container.Hostname, _container.GetMappedPublicPort(Port), "/xapi/").Uri;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>LRSQL_IMAGE from eng/conformance/cts.env (tag and digest), so CI and these tests run the same lrsql.</summary>
    private static string PinnedImage()
    {
        var path = typeof(LrsqlFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CtsEnvFile")
            .Value!;
        const string Prefix = "LRSQL_IMAGE=";
        return File.ReadLines(path).Single(line => line.StartsWith(Prefix, StringComparison.Ordinal)).Substring(Prefix.Length).Trim();
    }
}
