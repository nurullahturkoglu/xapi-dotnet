using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xapi.Client;
using Xapi.Client.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IXapiClient"/> with <see cref="IHttpClientFactory"/>.</summary>
public static class XapiClientServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> that <see cref="AddXapiClient"/> registers.</summary>
    public const string HttpClientName = "Xapi.Client";

    /// <summary>
    /// Registers <see cref="IXapiClient"/> and <see cref="XapiClient"/> as typed clients of a named
    /// <see cref="HttpClient"/> (<see cref="HttpClientName"/>), configured with <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is transient, as typed clients are; resolve it where it is used. A negotiated xAPI version
    /// (<see cref="XapiClientOptions.Version"/> left empty) is shared by every instance, so the About resource is
    /// asked once per service provider.
    /// </para>
    /// <para>
    /// The client does not retry. To retry transient failures, add a resilience handler from the
    /// <c>Microsoft.Extensions.Http.Resilience</c> package to <see cref="IXapiClientBuilder.HttpClient"/>. POST of a
    /// Statement without an id is not idempotent, so either give Statements ids (PUT and POST with ids are safe
    /// to repeat: the LRS keeps the first) or keep retries to safe methods:
    /// </para>
    /// <code>
    /// services.AddXapiClient(o =>
    /// {
    ///     o.Endpoint = new Uri("https://lrs.example.com/xapi/");
    ///     o.Credentials = XapiCredentials.Basic(key, secret);
    /// })
    /// .HttpClient.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
    /// </code>
    /// <para>
    /// For delivery that survives restarts and long outages, use an outbox in front of the client instead of long
    /// retry chains.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets <see cref="XapiClientOptions.Endpoint"/>, credentials and the other options.</param>
    /// <returns>A builder that exposes the <see cref="IHttpClientBuilder"/>.</returns>
    public static IXapiClientBuilder AddXapiClient(this IServiceCollection services, Action<XapiClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<XapiClientOptions>().Configure(configure);
        services.AddSingleton(static _ => new VersionNegotiation());
        services.TryAddSingleton(static provider => provider.GetService<IMeterFactory>() is { } meterFactory
            ? new ClientMetrics(meterFactory.Create(XapiClientDiagnostics.MeterName))
            : ClientMetrics.Shared);
        var httpClient = services.AddHttpClient(HttpClientName);
        httpClient.AddTypedClient(static (http, provider) => new XapiClient(
                http,
                provider.GetRequiredService<IOptions<XapiClientOptions>>().Value,
                provider.GetRequiredService<VersionNegotiation>(),
                provider.GetRequiredService<ClientMetrics>()));
        services.AddTransient<IXapiClient>(static provider => provider.GetRequiredService<XapiClient>());

        return new Builder(services, httpClient);
    }

    private sealed class Builder(IServiceCollection services, IHttpClientBuilder httpClient) : IXapiClientBuilder
    {
        public IServiceCollection Services { get; } = services;

        public IHttpClientBuilder HttpClient { get; } = httpClient;
    }
}
