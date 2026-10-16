using Microsoft.AspNetCore.Builder;
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
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddXapiLrs(o => o.MigrateOnStartup = false), start: false);

        var exception = Assert.Throws<InvalidOperationException>(app.Start);

        Assert.Contains("MigrateOnStartup", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Credentials_reject_keys_with_a_colon_and_duplicates()
    {
        var options = new XapiLrsOptions();
        options.Credentials.Add("key", "secret", XapiScope.All);

        Assert.Throws<ArgumentException>(() => options.Credentials.Add("a:b", "secret", XapiScope.All));
        Assert.Throws<ArgumentException>(() => options.Credentials.Add("key", "other", XapiScope.All));
        Assert.Throws<ArgumentException>(() => options.Credentials.Add("none", "secret"));
    }
}
