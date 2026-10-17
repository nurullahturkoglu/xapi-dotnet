using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Tests.Infrastructure;

// samples/LrsHost on a database of its own, with one Basic credential per scope combination the tests need.
public sealed class LrsApp : IAsyncDisposable
{
    public const string AuthorityHomePage = "https://lrs.example.com/";

    private readonly Factory _factory;
    private readonly bool _kestrel;

    // kestrel: run on a real Kestrel server on a loopback port instead of the in-memory TestServer, for what
    // only a real server does (request body limits)
    internal LrsApp(TestDatabase database, string connectionString, Action<IServiceCollection>? configure, Action<KestrelServerOptions>? kestrel = null)
    {
        Database = database;
        _factory = new Factory(database, connectionString, configure);
        if (kestrel is not null)
        {
            _kestrel = true;
            _factory.UseKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                kestrel(options);
            });
        }
    }

    public TestDatabase Database { get; }

    public IServiceProvider Services => _factory.Services;

    // Starts the host: migrations and credential seeding run here
    public void Start()
    {
        if (_kestrel)
        {
            _factory.StartServer();
        }
        else
        {
            _ = _factory.Server;
        }
    }

    public HttpClient Anonymous(string? version = "2.0.0")
    {
        var client = _factory.CreateClient();
        if (version is not null)
        {
            client.DefaultRequestHeaders.Add("X-Experience-API-Version", version);
        }

        return client;
    }

    public HttpClient Client(string key = Credentials.All, string? version = "2.0.0")
    {
        var client = Anonymous(version);
        client.DefaultRequestHeaders.Authorization = Basic(key, Credentials.SecretOf(key));
        return client;
    }

    public static AuthenticationHeaderValue Basic(string key, string secret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}")));

    internal XapiLrsDbContext CreateDbContext() =>
        Services.GetRequiredService<IDbContextFactory<XapiLrsDbContext>>().CreateDbContext();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    public static class Credentials
    {
        public const string All = "all";
        public const string Writer = "writer";
        public const string Reader = "reader";
        public const string Mine = "mine";
        public const string State = "state";
        public const string Profile = "profile";
        public const string AllRead = "allread";

        public static string SecretOf(string key) => key + "-secret";
    }

    private sealed class Factory(TestDatabase database, string connectionString, Action<IServiceCollection>? configure)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Lrs:Database", database == TestDatabase.Sqlite ? "sqlite" : "postgres");
            builder.UseSetting("Lrs:ConnectionString", connectionString);
            builder.UseSetting("Lrs:Key", Credentials.All);
            builder.UseSetting("Lrs:Secret", Credentials.SecretOf(Credentials.All));
            builder.UseSetting("Lrs:AuthorityHomePage", AuthorityHomePage);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.ConfigureTestServices(services =>
            {
                services.AddXapiLrs(options =>
                {
                    options.Credentials.Add(Credentials.Writer, Credentials.SecretOf(Credentials.Writer), XapiScope.StatementsWrite);
                    options.Credentials.Add(Credentials.Reader, Credentials.SecretOf(Credentials.Reader), XapiScope.StatementsRead);
                    options.Credentials.Add(Credentials.Mine, Credentials.SecretOf(Credentials.Mine), XapiScope.StatementsWrite, XapiScope.StatementsReadMine);
                    options.Credentials.Add(Credentials.State, Credentials.SecretOf(Credentials.State), XapiScope.State);
                    options.Credentials.Add(Credentials.Profile, Credentials.SecretOf(Credentials.Profile), XapiScope.Profile);
                    options.Credentials.Add(Credentials.AllRead, Credentials.SecretOf(Credentials.AllRead), XapiScope.AllRead);
                });
                configure?.Invoke(services);
            });
        }
    }
}
