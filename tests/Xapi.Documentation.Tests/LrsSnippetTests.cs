using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Documentation.Tests.Snippets;
using Xapi.Lrs.Storage;

namespace Xapi.Documentation.Tests;

#pragma warning disable XAPI0001 // checks the database through XapiLrsDbContext
public class LrsSnippetTests
{
    [Fact]
    public async Task The_migration_step_creates_the_database_and_returns()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("xapi-docs-");
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Services.AddXapiLrs()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory.FullName, "lrs.db"), Pooling = false }.ToString());
            await using var app = builder.Build();

            await LrsSnippets.MigrateOnly(app, ["--migrate"]);

            await using var context = await app.Services.GetRequiredService<IDbContextFactory<XapiLrsDbContext>>().CreateDbContextAsync(ct);
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(ct));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(ct));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
