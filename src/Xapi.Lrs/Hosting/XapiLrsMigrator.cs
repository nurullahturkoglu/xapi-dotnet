using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Hosting;

// Shared by MigrateOnStartup (XapiLrsStartup) and MigrateXapiLrsDatabaseAsync, so both apply the same migrations
// and log the same way.
internal sealed partial class XapiLrsMigrator(
    IDbContextFactory<XapiLrsDbContext> contextFactory,
    ILogger<XapiLrsMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            LogMigrating(logger);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying xAPI LRS database migrations")]
    private static partial void LogMigrating(ILogger logger);
}
