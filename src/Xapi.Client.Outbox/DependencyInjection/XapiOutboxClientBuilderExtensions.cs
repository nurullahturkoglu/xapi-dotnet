using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xapi.Client;
using Xapi.Client.Outbox;
using Xapi.Client.Outbox.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds a <see cref="StatementOutbox"/> in front of the client registered by <c>AddXapiClient</c>, and a hosted
/// service that delivers its Statements in the background.
/// </summary>
public static class XapiOutboxClientBuilderExtensions
{
    /// <summary>
    /// Registers a <see cref="StatementOutbox"/> singleton over a <see cref="FileOutboxStore"/> in
    /// <paramref name="directory"/>, and a hosted service that runs <see cref="StatementOutbox.RunAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outbox sends with the <see cref="XapiClient"/> registered by <c>AddXapiClient</c>, resolved again for
    /// every flush so that <see cref="IHttpClientFactory"/> can rotate handlers. Inject
    /// <see cref="StatementOutbox"/> where Statements are produced:
    /// </para>
    /// <code>
    /// services.AddXapiClient(o =>
    /// {
    ///     o.Endpoint = new Uri("https://lrs.example.com/xapi/");
    ///     o.Credentials = XapiCredentials.Basic(key, secret);
    /// })
    /// .AddOutbox("data/xapi-outbox");
    ///
    /// // in a request handler
    /// await outbox.EnqueueAsync(statement, cancellationToken);
    /// </code>
    /// <para>
    /// Service factories cannot be asynchronous, so the store is opened with blocking reads when the outbox is
    /// first resolved, at the latest when the host starts its hosted services. To open it before that, pass a
    /// store from <see cref="FileOutboxStore.OpenAsync"/> to
    /// <see cref="AddOutbox(IXapiClientBuilder, Func{IServiceProvider, OutboxStore}, Action{OutboxOptions})"/>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The builder returned by <c>AddXapiClient</c>.</param>
    /// <param name="directory">The directory of the <see cref="FileOutboxStore"/>; used by this application only.</param>
    /// <param name="configure">Changes the <see cref="OutboxOptions"/>, if needed.</param>
    /// <returns>The builder.</returns>
    public static IXapiClientBuilder AddOutbox(this IXapiClientBuilder builder, string directory, Action<OutboxOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return builder.AddOutbox(_ => FileOutboxStore.Open(directory), configure);
    }

    /// <summary>
    /// Registers a <see cref="StatementOutbox"/> singleton over the store <paramref name="storeFactory"/> creates,
    /// and a hosted service that runs <see cref="StatementOutbox.RunAsync"/>.
    /// </summary>
    /// <remarks>
    /// The store is registered as the <see cref="OutboxStore"/> singleton, so the container disposes it if it is
    /// disposable. See <see cref="AddOutbox(IXapiClientBuilder, string, Action{OutboxOptions})"/> for the client.
    /// </remarks>
    /// <param name="builder">The builder returned by <c>AddXapiClient</c>.</param>
    /// <param name="storeFactory">Creates the store, once.</param>
    /// <param name="configure">Changes the <see cref="OutboxOptions"/>, if needed.</param>
    /// <returns>The builder.</returns>
    public static IXapiClientBuilder AddOutbox(this IXapiClientBuilder builder, Func<IServiceProvider, OutboxStore> storeFactory, Action<OutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var services = builder.Services;
        var options = services.AddOptions<OutboxOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(storeFactory);
        services.TryAddSingleton(static provider => new StatementOutbox(
            provider.GetRequiredService<XapiClient>,
            provider.GetRequiredService<OutboxStore>(),
            provider.GetRequiredService<IOptions<OutboxOptions>>().Value,
            provider.GetService<IMeterFactory>() is { } meterFactory
                ? new OutboxMetrics(meterFactory.Create(XapiClientDiagnostics.MeterName))
                : null));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StatementOutboxService>());
        return builder;
    }
}
