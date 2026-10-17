using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Hosting;

// AddXapiLrsHealthCheck. Pending migrations count as a failure, not as Degraded: the requests that need the missing
// tables or columns fail, so a readiness probe should keep the instance out of rotation until the migration step
// has run. An instance older than the database is not affected, because EF Core only reports the migrations it
// knows and has not found applied.
internal sealed class XapiLrsHealthCheck(IDbContextFactory<XapiLrsDbContext>? contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var failure = context.Registration.FailureStatus;
        if (contextFactory is null)
        {
            return new HealthCheckResult(failure, "The xAPI LRS is not registered. Call services.AddXapiLrs() and choose a database.");
        }

        try
        {
            var database = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (database.ConfigureAwait(false))
            {
                if (!await database.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    return new HealthCheckResult(failure, "The xAPI LRS database cannot be reached.");
                }

                var pending = (await database.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();
                if (pending > 0)
                {
                    return new HealthCheckResult(failure, $"The xAPI LRS database has {pending} migration(s) that are not applied. Run MigrateXapiLrsDatabaseAsync or set MigrateOnStartup.");
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception goes into the report, which the default health endpoint does not write out
            return new HealthCheckResult(failure, "The xAPI LRS database cannot be reached.", exception);
        }

        return HealthCheckResult.Healthy("The xAPI LRS database is reachable and migrated.");
    }
}
