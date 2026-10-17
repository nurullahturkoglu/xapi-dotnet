using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;

namespace Xapi.Lrs.Tests;

public class HostingTests(LrsHosts hosts)
{
    [Fact]
    public async Task Sqlite_uses_wal_and_takes_the_write_lock_when_the_transaction_begins()
    {
        var ct = TestContext.Current.CancellationToken;
        var app = await hosts.CreateAsync(TestDatabase.Sqlite);
        var provider = app.Services.GetRequiredService<XapiLrsDatabaseProvider>();
        await using var context = app.CreateDbContext();
        await using var transaction = await provider.BeginWriteTransactionAsync(context, ct);

        using (var journal = context.Database.GetDbConnection().CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode";
            journal.Transaction = transaction.GetDbTransaction();
            Assert.Equal("wal", (string?)await journal.ExecuteScalarAsync(ct));
        }

        // A deferred BEGIN would only lock on the first write, so this second writer would get in
        await using var other = new SqliteConnection(context.Database.GetConnectionString());
        await other.OpenAsync(ct);
        using var begin = other.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE";
        begin.CommandTimeout = 1;
        var busy = await Assert.ThrowsAsync<SqliteException>(() => begin.ExecuteNonQueryAsync(ct));
        Assert.Equal(5, busy.SqliteErrorCode);
    }

    [Fact]
    public async Task Startup_explains_that_the_database_is_not_migrated()
    {
        // Built directly: WebApplicationFactory sometimes disposes a host that fails to start before rethrowing,
        // and the test then sees ObjectDisposedException instead of the startup error
        var database = Path.Combine(Path.GetTempPath(), $"xapi-unmigrated-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddXapiLrs(o =>
            {
                o.MigrateOnStartup = false;
                o.Credentials.Add(new XapiLrsCredential("key", "secret", XapiScopes.All));
            })
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains("MigrateOnStartup", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public async Task MigrateXapiLrsDatabaseAsync_creates_the_database_so_the_host_starts_without_MigrateOnStartup()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = Path.Combine(Path.GetTempPath(), $"xapi-migrate-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddXapiLrs(o => o.Credentials.Add(new XapiLrsCredential("key", "secret", XapiScopes.All)))
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();

        try
        {
            await app.Services.MigrateXapiLrsDatabaseAsync(ct);

            await using (var context = await app.Services.GetRequiredService<IDbContextFactory<XapiLrsDbContext>>().CreateDbContextAsync(ct))
            {
                Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(ct));
                Assert.Empty(await context.Database.GetPendingMigrationsAsync(ct));
            }

            await app.StartAsync(ct);
            await app.StopAsync(ct);
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public async Task MigrateXapiLrsDatabaseAsync_without_adding_the_lrs_fails()
    {
        await using var app = WebApplication.CreateSlimBuilder().Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Services.MigrateXapiLrsDatabaseAsync(TestContext.Current.CancellationToken));

        Assert.Contains("AddXapiLrs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_the_lrs_without_a_database_fails_with_a_clear_message()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddXapiLrs();
        using var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IDbContextFactory<XapiLrsDbContext>>().CreateDbContext());

        Assert.Contains("UseSqlite or UsePostgreSql", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_the_endpoints_without_adding_the_lrs_fails()
    {
        var builder = WebApplication.CreateSlimBuilder();
        using var app = builder.Build();

        Assert.Throws<InvalidOperationException>(() => app.MapXapiLrs());
    }
}
