using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xapi.Client;
using Xapi.Client.Outbox;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="StatementOutbox"/> and a hosted service that delivers its Statements in the background.</summary>
public static class XapiOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="StatementOutbox"/> singleton over a <see cref="FileOutboxStore"/> in
    /// <paramref name="directory"/>, and a hosted service that runs <see cref="StatementOutbox.RunAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outbox sends with the <see cref="IXapiClient"/> registered by <c>AddXapiClient</c>, resolved again for
    /// every flush so that <see cref="IHttpClientFactory"/> can rotate handlers. Inject
    /// <see cref="StatementOutbox"/> where Statements are produced:
    /// </para>
    /// <code>
    /// services.AddXapiClient(o =>
    /// {
    ///     o.Endpoint = new Uri("https://lrs.example.com/xapi/");
    ///     o.Credentials = XapiCredentials.Basic(key, secret);
    /// });
    /// services.AddXapiOutbox("data/xapi-outbox");
    ///
    /// // in a request handler
    /// await outbox.EnqueueAsync(statement, cancellationToken);
    /// </code>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="directory">The directory of the <see cref="FileOutboxStore"/>; used by this application only.</param>
    /// <param name="configure">Changes the <see cref="OutboxOptions"/>, if needed.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddXapiOutbox(this IServiceCollection services, string directory, Action<OutboxOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return services.AddXapiOutbox(_ => new FileOutboxStore(directory), configure);
    }

    /// <summary>
    /// Registers a <see cref="StatementOutbox"/> singleton over the store <paramref name="storeFactory"/> creates,
    /// and a hosted service that runs <see cref="StatementOutbox.RunAsync"/>.
    /// </summary>
    /// <remarks>
    /// The store is registered as the <see cref="IOutboxStore"/> singleton, so the container disposes it if it is
    /// disposable. See <see cref="AddXapiOutbox(IServiceCollection, string, Action{OutboxOptions})"/> for the client.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="storeFactory">Creates the store, once.</param>
    /// <param name="configure">Changes the <see cref="OutboxOptions"/>, if needed.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddXapiOutbox(this IServiceCollection services, Func<IServiceProvider, IOutboxStore> storeFactory, Action<OutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var options = services.AddOptions<OutboxOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(storeFactory);
        services.TryAddSingleton(static provider => new StatementOutbox(
            provider.GetRequiredService<IXapiClient>,
            provider.GetRequiredService<IOutboxStore>(),
            provider.GetRequiredService<IOptions<OutboxOptions>>().Value));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StatementOutboxService>());
        return services;
    }
}
