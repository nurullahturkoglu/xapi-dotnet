using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xapi.Lrs;
using Xapi.Lrs.Hosting;
using Xapi.Lrs.Security;
using Xapi.Lrs.Statements;
using Xapi.Lrs.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the xAPI LRS services.</summary>
public static class XapiLrsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the LRS services and the Basic authentication scheme
    /// (<see cref="XapiLrsAuthenticationDefaults.AuthenticationScheme"/>). Choose a database on the returned
    /// builder (<c>UseSqlite</c> or <c>UsePostgreSql</c>) and map the endpoints with <c>MapXapiLrs</c>.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Configures the options, such as credentials.</param>
    /// <returns>A builder for further configuration.</returns>
    public static IXapiLrsBuilder AddXapiLrs(this IServiceCollection services, Action<XapiLrsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<XapiLrsOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        if (services.Any(d => d.ServiceType == typeof(XapiLrsMarker)))
        {
            return new XapiLrsBuilder(services);
        }

        services.AddSingleton<XapiLrsMarker>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<XapiLrsDbContext>((provider, builder) =>
        {
            var database = provider.GetService<XapiLrsDatabaseProvider>()
                ?? throw new InvalidOperationException("No database is configured for the xAPI LRS. Call UseSqlite or UsePostgreSql on the builder returned by AddXapiLrs.");
            database.Configure(builder);
        });
        services.TryAddSingleton<IStatementStore, EfStatementStore>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<CallerFactory>();
        services.AddSingleton<StatementWriteTracker>();
        services.AddSingleton<StatementWriter>();
        services.AddHostedService<XapiLrsStartup>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(XapiLrsAuthenticationDefaults.AuthenticationScheme, displayName: null, configureOptions: null);

        return new XapiLrsBuilder(services);
    }

    private sealed class XapiLrsBuilder(IServiceCollection services) : IXapiLrsBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
