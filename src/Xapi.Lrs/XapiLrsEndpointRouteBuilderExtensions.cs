using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Http;
using Xapi.Lrs.Hosting;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the xAPI resources onto an application's endpoints.</summary>
public static class XapiLrsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the xAPI LRS resources under <paramref name="prefix"/>, for example <c>/xapi/statements</c> and
    /// <c>/xapi/about</c>. Requires <c>AddXapiLrs</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder, usually the <c>WebApplication</c>.</param>
    /// <param name="prefix">The base path of the LRS. Clients use it as their xAPI endpoint.</param>
    /// <returns>The route group, to add authorization policies, rate limiting, CORS and similar conventions.</returns>
    /// <exception cref="InvalidOperationException"><c>AddXapiLrs</c> was not called.</exception>
    public static RouteGroupBuilder MapXapiLrs(this IEndpointRouteBuilder endpoints, string prefix = "/xapi")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(prefix);
        if (endpoints.ServiceProvider.GetService<XapiLrsMarker>() is null)
        {
            throw new InvalidOperationException("Call services.AddXapiLrs() before MapXapiLrs().");
        }

        var group = endpoints.MapGroup(prefix);
        group.AddEndpointFilter<XapiRequestFilter>();

        string[] read = [HttpMethods.Get, HttpMethods.Head];
        group.MapMethods("/about", read, AboutEndpoint.Get)
            .WithMetadata(new XapiEndpointMetadata(XapiAccess.Anonymous, VersionHeaderRequired: false, IsStatementResource: false));
        group.MapPut("/statements", StatementEndpoints.PutAsync)
            .WithMetadata(new XapiEndpointMetadata(XapiAccess.StatementsWrite, VersionHeaderRequired: true, IsStatementResource: true));
        group.MapPost("/statements", StatementEndpoints.PostAsync)
            .WithMetadata(new XapiEndpointMetadata(XapiAccess.StatementsWrite, VersionHeaderRequired: true, IsStatementResource: true));
        group.MapMethods("/statements", read, StatementEndpoints.GetAsync)
            .WithMetadata(new XapiEndpointMetadata(XapiAccess.StatementsRead, VersionHeaderRequired: true, IsStatementResource: true));

        // Unknown paths and methods still get an xAPI error with the version header (4.1.7: every response)
        group.Map("/{**path}", FallbackEndpoint.Handle)
            .WithMetadata(new XapiEndpointMetadata(XapiAccess.Authenticated, VersionHeaderRequired: true, IsStatementResource: false));

        return group;
    }
}
