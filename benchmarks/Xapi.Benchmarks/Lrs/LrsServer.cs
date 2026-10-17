using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xapi.Lrs;

namespace Xapi.Benchmarks.Lrs;

// Xapi.Lrs in an ASP.NET Core app on TestServer: requests go through the whole middleware and endpoint pipeline
// and the real database, but not through sockets. Each server gets a fresh database.
public sealed class LrsServer : IAsyncDisposable
{
    private const string Key = "bench";
    private const string Secret = "bench-secret";

    private readonly WebApplication _app;
    private readonly string? _sqliteFile;

    private LrsServer(WebApplication app, HttpClient client, RetryCounter retries, string? sqliteFile)
    {
        _app = app;
        Client = client;
        Retries = retries;
        _sqliteFile = sqliteFile;
    }

    public HttpClient Client { get; }

    public RetryCounter Retries { get; }

    public static async Task<LrsServer> StartAsync(string database)
    {
        var name = "xapi_bench_" + Guid.NewGuid().ToString("N");
        string? sqliteFile = null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        // Only the statement store's retry message is wanted; it is logged at Debug
        var retries = new RetryCounter();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(retries);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter(RetryCounter.Category, LogLevel.Debug);

        var lrs = builder.Services.AddXapiLrs(options =>
        {
            options.MigrateOnStartup = true;
            options.Credentials.Add(Key, Secret, XapiScopes.All);
        });
        switch (database)
        {
            case LrsDatabases.Sqlite:
                sqliteFile = Path.Combine(Path.GetTempPath(), name + ".db");
                lrs.UseSqlite(new SqliteConnectionStringBuilder { DataSource = sqliteFile }.ToString());
                break;
            case LrsDatabases.PostgreSql:
                // A database per server; the migrations create it
                lrs.UsePostgreSql(new NpgsqlConnectionStringBuilder(LrsDatabases.PostgreSqlConnectionString) { Database = name }.ToString());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(database), database, "Unknown database.");
        }

        var app = builder.Build();
        app.MapXapiLrs("/xapi");
        await app.StartAsync().ConfigureAwait(false);

        var client = app.GetTestClient();
        client.BaseAddress = new Uri(client.BaseAddress!, "/xapi/");
        client.DefaultRequestHeaders.Add("X-Experience-API-Version", "2.0.0");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Key}:{Secret}")));
        return new LrsServer(app, client, retries, sqliteFile);
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public async Task PostAsync(string json)
    {
        using var content = Json(json);
        using var response = await Client.PostAsync(new Uri("statements", UriKind.Relative), content).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        if (_sqliteFile is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _sqliteFile, _sqliteFile + "-wal", _sqliteFile + "-shm" })
            {
                File.Delete(file);
            }
        }
    }
}
