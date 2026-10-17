using Xapi.Lrs;
using Xapi.Lrs.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Runs xAPI LRS tasks on a built service provider.</summary>
public static class XapiLrsServiceProviderExtensions
{
    /// <summary>
    /// Applies the database migrations of the LRS added with <c>AddXapiLrs</c>, creating the database if it does
    /// not exist. This is what <see cref="XapiLrsOptions.MigrateOnStartup"/> runs when the host starts; call it
    /// instead to migrate as a separate deployment step, for example from a <c>--migrate</c> switch before
    /// <c>app.Run()</c>.
    /// </summary>
    /// <param name="services">The application's services, usually <c>app.Services</c>.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>A task that completes when every migration has been applied.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>AddXapiLrs</c> was not called, or no database was chosen with <c>UseSqlite</c> or <c>UsePostgreSql</c>.
    /// </exception>
    public static Task MigrateXapiLrsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var migrator = services.GetService<XapiLrsMigrator>()
            ?? throw new InvalidOperationException("Call services.AddXapiLrs() before MigrateXapiLrsDatabaseAsync().");
        return migrator.MigrateAsync(cancellationToken);
    }
}
