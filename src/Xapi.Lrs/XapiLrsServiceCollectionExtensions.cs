using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xapi.Lrs;
using Xapi.Lrs.Hosting;
using Xapi.Lrs.Http;
using Xapi.Lrs.Security;
using Xapi.Lrs.Statements;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Telemetry;

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
    /// The options are checked when the host starts, and invalid ones stop it with an
    /// <see cref="OptionsValidationException"/> that lists every problem: no credentials and no
    /// <see cref="XapiLrsOptions.AuthenticationScheme"/>, an empty or built-in scheme name, an empty
    /// <see cref="XapiLrsOptions.ScopeClaimType"/>, a relative <see cref="XapiLrsOptions.AuthorityHomePage"/>, a
    /// limit out of range, or an invalid credential (see <see cref="XapiLrsOptions.Credentials"/>).
    /// </remarks>
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
        services.AddMetrics();
        services.AddSingleton<XapiLrsMetrics>();
        services.AddSingleton<XapiLrsMigrator>();
        services.AddHostedService<XapiLrsStartup>();
        services.AddSingleton<XapiLrsPaths>();
        services.AddTransient<IStartupFilter, AlternateRequestSyntaxStartupFilter>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(XapiLrsAuthenticationDefaults.AuthenticationScheme, displayName: null, configureOptions: null);

        return new XapiLrsBuilder(services);
    }

    /// <summary>
    /// Adds the LRS services as <see cref="AddXapiLrs(IServiceCollection, Action{XapiLrsOptions}?)"/> does, with
    /// the options bound from a configuration section such as <c>Lrs</c> in <c>appsettings.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are the option names, for example <c>Lrs:MaxStatementsPerPage</c>, and each credential has a
    /// <c>Key</c>, a <c>Secret</c> and a <c>Scopes</c> array of xAPI scope names, for example
    /// <c>Lrs:Credentials:0:Scopes:0</c>. A key that is not an option name stops the host when the options are
    /// first read (<see cref="BinderOptions.ErrorOnUnknownConfiguration"/>), so a misspelt one does not go
    /// unnoticed.
    /// </para>
    /// <para>
    /// The configuration binder does not skip empty values, such as an empty environment variable: an empty
    /// number or switch fails to convert, with a message that names its key, and an empty
    /// <see cref="XapiLrsOptions.AuthorityHomePage"/> or text option is set to <see langword="null"/> or empty,
    /// which validation then reports. Remove a value rather than set it to empty to keep the default.
    /// </para>
    /// <para>
    /// <see cref="XapiLrsOptions.AuthorityFactory"/> and the authority of a credential cannot be read from
    /// configuration; set them in <paramref name="configure"/>, which runs after the section is bound. Credentials
    /// from configuration get the default authority, an account on <see cref="XapiLrsOptions.AuthorityHomePage"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The application's services.</param>
    /// <param name="configuration">The configuration section that holds the options.</param>
    /// <param name="configure">Configures the options further, after the section is bound.</param>
    /// <returns>A builder for further configuration.</returns>
    public static IXapiLrsBuilder AddXapiLrs(this IServiceCollection services, IConfiguration configuration, Action<XapiLrsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<XapiLrsOptions>().Bind(configuration, binder => binder.ErrorOnUnknownConfiguration = true);
        return services.AddXapiLrs(configure);
    }

    private sealed class XapiLrsBuilder(IServiceCollection services) : IXapiLrsBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
