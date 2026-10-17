using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xapi;
using Xapi.Client;
using Xapi.Lrs;

namespace FakeLrsInTests;

/// <summary>
/// A real xAPI LRS for tests: Xapi.Lrs hosted in the test process with <see cref="TestServer"/> (no port, no
/// Docker, no network) on a SQLite database in a temporary folder that is deleted afterwards.
/// </summary>
public sealed class FakeLrs : IAsyncLifetime
{
    private const string Key = "test";
    private const string Secret = "test-secret";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fake-lrs-" + Guid.NewGuid().ToString("N"));
    private readonly List<HttpClient> _httpClients = [];
    private WebApplication? _app;

    /// <summary>Starts the LRS; the database is created by its migrations.</summary>
    /// <returns>A task that completes when the LRS accepts requests.</returns>
    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services
            .AddXapiLrs(options =>
            {
                options.MigrateOnStartup = true;
                options.Credentials.Add(Key, Secret, XapiScope.All);
            })
            // Without pooling no connection stays open, so the folder can be deleted on every OS
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "lrs.db"), Pooling = false }.ToString());

        _app = builder.Build();
        _app.MapXapiLrs("/xapi");
        await _app.StartAsync();
    }

    /// <summary>Creates a client of this LRS, as the application would create one for a real LRS.</summary>
    /// <param name="version">The xAPI version to speak, or <see langword="null"/> to negotiate it.</param>
    /// <returns>The client.</returns>
    public IXapiClient CreateClient(XapiVersion? version = null)
    {
        var server = (_app ?? throw new InvalidOperationException("The LRS has not been started.")).GetTestServer();
        var http = server.CreateClient();
        _httpClients.Add(http);
        return new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri(server.BaseAddress, "xapi/"),
            Credentials = XapiCredentials.Basic(Key, Secret),
            Version = version,
        });
    }

    /// <summary>Stops the LRS and deletes its database.</summary>
    /// <returns>A task that completes when everything is cleaned up.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var http in _httpClients)
        {
            http.Dispose();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Directory.Delete(_directory, recursive: true);
    }
}
