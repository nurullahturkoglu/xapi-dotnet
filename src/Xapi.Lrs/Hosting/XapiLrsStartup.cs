using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xapi.Lrs.Security;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Hosting;

// Registered once by AddXapiLrs; MapXapiLrs checks for it.
internal sealed class XapiLrsMarker;

// Runs in StartingAsync, which completes before any hosted service (the web server included) starts, so no
// request reaches an unmigrated database or a credential list that has not been seeded yet.
internal sealed partial class XapiLrsStartup(
    IDbContextFactory<XapiLrsDbContext> contextFactory,
    CredentialStore credentials,
    IOptions<XapiLrsOptions> options,
    ILogger<XapiLrsStartup> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.MigrateOnStartup)
        {
            var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                LogMigrating(logger);
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (settings.AuthenticationScheme is null)
        {
            try
            {
                await credentials.SeedAsync(settings.Credentials, settings.AuthorityHomePage, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException)
            {
                throw new InvalidOperationException("The xAPI LRS could not store its credentials. Apply the database migrations first, or set MigrateOnStartup.", exception);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying xAPI LRS database migrations")]
    private static partial void LogMigrating(ILogger logger);
}
