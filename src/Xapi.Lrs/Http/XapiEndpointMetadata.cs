using Microsoft.AspNetCore.Http;
using Xapi.Lrs.Security;

namespace Xapi.Lrs.Http;

internal enum XapiAccess
{
    Anonymous,
    Authenticated,
    StatementsWrite,
    StatementsRead,
    StateRead,
    StateWrite,
    ProfileRead,
    ProfileWrite,
}

internal sealed record XapiEndpointMetadata(XapiAccess Access, bool VersionHeaderRequired, bool IsStatementResource);

// Per-request results of the filter, read by the handlers.
internal sealed class XapiRequestFeature(XapiVersion version, XapiCaller? caller)
{
    public XapiVersion Version { get; } = version;

    public XapiCaller Caller => caller ?? throw new InvalidOperationException("The endpoint does not authenticate.");

    public static XapiRequestFeature Get(HttpContext httpContext) =>
        httpContext.Features.Get<XapiRequestFeature>() ?? throw new InvalidOperationException("The xAPI endpoint filter did not run.");
}
