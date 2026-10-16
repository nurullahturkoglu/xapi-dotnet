namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Returned by <see cref="XapiClientServiceCollectionExtensions.AddXapiClient"/> to configure the client further.</summary>
public interface IXapiClientBuilder
{
    /// <summary>The service collection the client was added to.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The named <see cref="HttpClient"/> the client sends requests with. Add handlers, a primary handler, timeouts
    /// or resilience here.
    /// </summary>
    IHttpClientBuilder HttpClient { get; }
}
