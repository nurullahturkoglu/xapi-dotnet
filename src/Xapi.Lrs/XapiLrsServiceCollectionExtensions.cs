using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xapi.Lrs;
using Xapi.Lrs.Hosting;
using Xapi.Lrs.Http;
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
    /// <remarks>
    /// The options are checked when the host starts, and an invalid combination stops it with an
    /// <see cref="OptionsValidationException"/> that lists every problem: no credentials and no
    /// <see cref="XapiLrsOptions.AuthenticationScheme"/>, an empty or built-in scheme name, an empty
    /// <see cref="XapiLrsOptions.ScopeClaimType"/>, or a default <see cref="XapiLrsOptions.AuthorityHomePage"/>.
    /// </remarks>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">
    /// Configures the options, such as credentials. Call <see cref="XapiLrsOptions.Bind"/> in it to read them from
    /// configuration.
    /// </param>
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<XapiLrsOptions>, XapiLrsOptionsValidator>());
        options.ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<XapiLrsDbContext>((provider, builder) =>
        {
            var database = provider.GetService<XapiLrsDatabaseProvider>()
                ?? throw new InvalidOperationException("No database is configured for the xAPI LRS. Call UseSqlite or UsePostgreSql on the builder returned by AddXapiLrs.");
            database.Configure(builder);
        });
        services.TryAddSingleton<IStatementStore, EfStatementStore>();
        services.TryAddSingleton<IDocumentStore, EfDocumentStore>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<CallerFactory>();
        services.AddSingleton<StatementWriteTracker>();
        services.AddSingleton<StatementWriter>();
        services.AddSingleton<XapiLrsMigrator>();
        services.AddHostedService<XapiLrsStartup>();
        services.AddSingleton<XapiLrsPaths>();
        services.AddTransient<IStartupFilter, AlternateRequestSyntaxStartupFilter>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(XapiLrsAuthenticationDefaults.AuthenticationScheme, displayName: null, configureOptions: null);

        return new XapiLrsBuilder(services);
    }

    private sealed class XapiLrsBuilder(IServiceCollection services) : IXapiLrsBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
