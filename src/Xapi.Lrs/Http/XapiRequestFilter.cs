using System.Data.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xapi.Lrs.Security;
using Xapi.Lrs.Statements;

namespace Xapi.Lrs.Http;

// Runs before every xAPI endpoint: version negotiation, authentication, scope checks and the error format
// (ADR-010). Errors are returned as results so they carry the same headers as successful responses.
internal sealed partial class XapiRequestFilter(
    IOptions<XapiLrsOptions> options,
    CallerFactory callers,
    StatementWriteTracker writes,
    ILogger<XapiRequestFilter> logger) : IEndpointFilter
{
    private static readonly XapiEndpointMetadata Unannotated = new(XapiAccess.Authenticated, VersionHeaderRequired: true, IsStatementResource: false);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var metadata = http.GetEndpoint()?.Metadata.GetMetadata<XapiEndpointMetadata>() ?? Unannotated;
        var headers = http.Response.Headers;

        // IEEE 9274.1.1, 4.1.7: the header is on every response, errors included
        headers[XapiHeaders.Version] = XapiVersion.V2_0_0.HeaderValue;

        // Documents, attachments and error details carry client-supplied content; browsers must take the
        // content type as given instead of sniffing HTML out of it
        headers.XContentTypeOptions = "nosniff";
        if (metadata.IsStatementResource)
        {
            headers[XapiHeaders.ConsistentThrough] = XapiTimestamp.Format(writes.ConsistentThrough());
        }

        try
        {
            var requested = http.Request.Headers[XapiHeaders.Version];
            var version = XapiVersion.V2_0_0;
            if (requested.Count == 0)
            {
                if (metadata.VersionHeaderRequired)
                {
                    return XapiResults.Problem(StatusCodes.Status400BadRequest, $"The {XapiHeaders.Version} header is required.");
                }
            }
            else if (!TryNegotiate(requested, out version))
            {
                // The About resource answers whatever the header says (4.1.6.7)
                if (metadata.VersionHeaderRequired)
                {
                    return XapiResults.Problem(StatusCodes.Status400BadRequest, $"{XapiHeaders.Version} '{requested}' is not supported. Use {SupportedVersions}.");
                }

                version = XapiVersion.V2_0_0;
            }

            headers[XapiHeaders.Version] = version.HeaderValue;

            XapiCaller? caller = null;
            if (metadata.Access != XapiAccess.Anonymous)
            {
                var result = await http.AuthenticateAsync(callers.Scheme).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    if (callers.IsDelegated)
                    {
                        await http.ChallengeAsync(callers.Scheme).ConfigureAwait(false);
                        return Results.Empty;
                    }

                    headers[HeaderNames.WWWAuthenticate] = BasicAuthenticationHandler.Challenge;
                    return XapiResults.Problem(StatusCodes.Status401Unauthorized, result.Failure?.Message ?? "Authentication is required.");
                }

                caller = callers.Create(result.Principal);
                if (caller is null)
                {
                    return XapiResults.Problem(StatusCodes.Status403Forbidden, "No authority could be derived from the authenticated identity.");
                }

                if (!IsAllowed(metadata.Access, caller.Scopes))
                {
                    return XapiResults.Problem(StatusCodes.Status403Forbidden, "The credentials do not have a scope that allows this request.");
                }
            }

            http.Features.Set(new XapiRequestFeature(version, caller));
            return await next(context).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception)
        {
            // Body too large (413) or a broken request stream, reported by the server while reading the body
            return XapiResults.Problem(exception.StatusCode, exception.Message);
        }
        catch (Exception exception) when (IsProgramLimit(exception) && !http.Response.HasStarted)
        {
            // PostgreSQL cannot index a value of more than about 2.7 KB, so a very long IRI, agent identifier or
            // document id fails the write. That is the client's input, not a server error.
            return XapiResults.Problem(StatusCodes.Status400BadRequest, "A value in the request, such as an IRI, an agent identifier or a document id, is too long for this LRS to store.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !http.Response.HasStarted)
        {
            LogUnhandled(logger, exception, http.Request.Method, http.Request.Path);
            return XapiResults.Problem(StatusCodes.Status500InternalServerError, "The LRS could not process the request.");
        }
    }

    // SQLSTATE class 54, "program limit exceeded" (PostgreSQL 54000 for an index row over the btree maximum)
    private static bool IsProgramLimit(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } state } && state.StartsWith("54", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string SupportedVersions => options.Value.EnableVersion1_0_3 ? "2.0.x or 1.0.x" : "2.0.x";

    private bool TryNegotiate(Microsoft.Extensions.Primitives.StringValues requested, out XapiVersion version)
    {
        version = default;
        return requested.Count == 1 &&
               XapiVersion.TryParseHeader(requested[0], out version) &&
               (version.IsV2 || options.Value.EnableVersion1_0_3);
    }

    private static bool IsAllowed(XapiAccess access, ScopeSet scopes) => access switch
    {
        XapiAccess.StatementsWrite => Scopes.CanWriteStatements(scopes),
        XapiAccess.StatementsRead => Scopes.CanReadStatements(scopes),
        XapiAccess.StateRead => Scopes.CanUseState(scopes, write: false),
        XapiAccess.StateWrite => Scopes.CanUseState(scopes, write: true),
        XapiAccess.ProfileRead => Scopes.CanUseProfiles(scopes, write: false),
        XapiAccess.ProfileWrite => Scopes.CanUseProfiles(scopes, write: true),
        _ => true,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in xAPI request {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
