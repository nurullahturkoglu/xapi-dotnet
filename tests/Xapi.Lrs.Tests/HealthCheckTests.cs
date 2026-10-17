using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xapi.Lrs.Tests.Infrastructure;

namespace Xapi.Lrs.Tests;

public class HealthCheckTests(LrsHosts hosts)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sample_host_reports_healthy_when_the_database_is_reachable_and_migrated()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var client = app.Anonymous(version: null);

        using var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Database_that_cannot_be_opened_is_unhealthy()
    {
        var missing = Path.Combine(Path.GetTempPath(), "xapi-no-such-directory-" + Guid.NewGuid().ToString("N"), "lrs.db");
        await using var app = Build(missing, health => health.AddXapiLrsHealthCheck());

        var entry = await CheckAsync(app);

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("cannot be reached", entry.Description, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));
    }

    // EF Core checks that a SQLite database exists on a read-only connection, which cannot switch the journal mode:
    // a file that is not in WAL mode yet, such as one another tool created, must still report the missing migrations
    [Theory]
    [InlineData("DELETE")]
    [InlineData("WAL")]
    public async Task Database_with_pending_migrations_reports_the_failure_status(string journalMode)
    {
        var database = Path.Combine(Path.GetTempPath(), $"xapi-health-{Guid.NewGuid():N}.db");
        try
        {
            // Reachable, but no migration of the LRS is applied
            await using (var connection = new SqliteConnection(ConnectionString(database)))
            {
                await connection.OpenAsync(Ct);
                await using var create = connection.CreateCommand();
                create.CommandText = $"PRAGMA journal_mode = {journalMode}; CREATE TABLE other (id INTEGER)";
                await create.ExecuteNonQueryAsync(Ct);
            }

            await using var app = Build(database, health => health.AddXapiLrsHealthCheck(failureStatus: HealthStatus.Degraded, tags: ["ready"]));

            var entry = await CheckAsync(app);

            Assert.Equal(HealthStatus.Degraded, entry.Status);
            Assert.Contains("migration(s) that are not applied", entry.Description, StringComparison.Ordinal);
            Assert.Equal(["ready"], entry.Tags);

            await app.Services.MigrateXapiLrsDatabaseAsync(Ct);
            Assert.Equal(HealthStatus.Healthy, (await CheckAsync(app)).Status);

            await using var check = new SqliteConnection(ConnectionString(database));
            await check.OpenAsync(Ct);
            await using var journal = check.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode";
            Assert.Equal("wal", (string?)await journal.ExecuteScalarAsync(Ct));
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public async Task Check_without_the_lrs_explains_what_is_missing()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHealthChecks().AddXapiLrsHealthCheck("lrs");
        await using var app = builder.Build();

        var entry = await CheckAsync(app, "lrs");

        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("AddXapiLrs", entry.Description, StringComparison.Ordinal);
    }

    private static WebApplication Build(string database, Action<IHealthChecksBuilder> health)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services
            .AddXapiLrs(o => o.Credentials.Add(new XapiCredential("key", "secret", [XapiScope.All])))
            .UseSqlite(ConnectionString(database));
        health(builder.Services.AddHealthChecks());
        return builder.Build();
    }

    private static string ConnectionString(string database) =>
        new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();

    private static async Task<HealthReportEntry> CheckAsync(WebApplication app, string name = "xapi-lrs")
    {
        var report = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        return report.Entries[name];
    }
}
