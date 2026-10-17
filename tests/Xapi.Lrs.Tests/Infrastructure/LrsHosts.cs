using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xapi.Lrs.Tests.Infrastructure;

[assembly: AssemblyFixture(typeof(LrsHosts))]

namespace Xapi.Lrs.Tests.Infrastructure;

// One shared LRS per database for tests that only add statements with fresh ids, plus fresh LRSs for tests that
// change the configuration. The PostgreSQL container starts on first use and is shared by all of them.
public sealed class LrsHosts : IAsyncLifetime
{
    // Same image as the conformance job (eng/conformance/cts.env)
    private const string PostgreSqlImage = "postgres:17.11-alpine@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xapi-lrs-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<TestDatabase, LrsApp> _shared = [];
    private readonly List<LrsApp> _apps = [];
    private PostgreSqlContainer? _postgres;
    private int _databases;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    public async Task<LrsApp> SharedAsync(TestDatabase database)
    {
        await _lock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (!_shared.TryGetValue(database, out var app))
            {
                app = await CreateLockedAsync(database, configure: null);
                _shared[database] = app;
            }

            return app;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LrsApp> CreateAsync(TestDatabase database, Action<IServiceCollection>? configure = null, bool start = true, Action<KestrelServerOptions>? kestrel = null)
    {
        await _lock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            return await CreateLockedAsync(database, configure, start, kestrel);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<LrsApp> CreateLockedAsync(TestDatabase database, Action<IServiceCollection>? configure, bool start = true, Action<KestrelServerOptions>? kestrel = null)
    {
        var name = "lrs" + Interlocked.Increment(ref _databases);
        string connectionString;
        if (database == TestDatabase.Sqlite)
        {
            connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, name + ".db") }.ToString();
        }
        else
        {
            if (_postgres is null)
            {
                _postgres = new PostgreSqlBuilder(PostgreSqlImage).Build();
                await _postgres.StartAsync(TestContext.Current.CancellationToken);
            }

            // A database per app; the migrations create it
            connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ToString();
        }

        var app = new LrsApp(database, connectionString, configure, kestrel);
        _apps.Add(app);
        if (start)
        {
            app.Start();
        }

        return app;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var app in _apps)
        {
            await app.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on Windows; it is in the temp folder
        }

        _lock.Dispose();
    }
}
