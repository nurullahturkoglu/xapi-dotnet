using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Xapi.Lrs.Http;

// xAPI 1.0.3 Alternate Request Syntax (Communication 1.3): a POST with the intended method in the "method" query
// parameter and everything else (other parameters, some headers, the content) as form fields. xAPI 2.0
// (IEEE 9274.1.1) dropped it, so such a request is only rewritten when it is not a 2.0 request; otherwise it
// reaches the endpoints unchanged and is rejected there for its unrecognized "method" parameter (4.1.5).
internal static class AlternateRequestSyntax
{
    public const string MethodParameter = "method";

    public static string RejectionMessage(XapiVersion version) => version.IsV2
        ? "xAPI 2.0 has no Alternate Request Syntax, so the method parameter is not recognized. Send the request with its own HTTP method."
        : "An Alternate Request Syntax request must be a POST whose only query parameter is method, with the other parameters, the headers it carries and the content as application/x-www-form-urlencoded fields (xAPI 1.0.3, Communication 1.3).";
}

// The path prefixes MapXapiLrs mapped, so the rewriting only touches LRS requests.
internal sealed class XapiLrsPaths
{
    private readonly List<TemplateMatcher> _matchers = [];
    private readonly Lock _gate = new();

    public void Add(string prefix)
    {
        var pattern = RoutePatternFactory.Parse(prefix.Trim('/') + "/{**xapiResource}");
        lock (_gate)
        {
            _matchers.Add(new TemplateMatcher(new RouteTemplate(pattern), []));
        }
    }

    public bool Contains(PathString path)
    {
        lock (_gate)
        {
            return _matchers.Any(m => m.TryMatch(path, []));
        }
    }
}

// Runs first in the pipeline (before routing and authentication) so the rewritten request is routed and
// authenticated like an ordinary one.
internal sealed class AlternateRequestSyntaxStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<AlternateRequestSyntaxMiddleware>();
        next(app);
    };
}

internal sealed class AlternateRequestSyntaxMiddleware(RequestDelegate next, XapiLrsPaths paths, IOptions<XapiLrsOptions> options)
{
    // Form fields the LRS treats as headers (Communication 1.3.s3.b6, b7)
    private static readonly string[] HeaderFields =
    [
        HeaderNames.Authorization, XapiHeaders.Version, HeaderNames.ContentType, HeaderNames.ContentLength,
        HeaderNames.IfMatch, HeaderNames.IfNoneMatch,
    ];

    private const string ContentField = "content";

    public async Task InvokeAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var request = http.Request;
        if (options.Value.EnableVersion1_0_3 &&
            HttpMethods.IsPost(request.Method) &&
            request.Query.Count == 1 &&
            request.Query.TryGetValue(AlternateRequestSyntax.MethodParameter, out var methods) &&
            methods.Count == 1 &&
            Method(methods[0]) is { } method &&
            IsFormOrEmpty(request) &&
            paths.Contains(request.Path))
        {
            // The form is read before authentication, since the credentials can be one of its fields, so it has a
            // limit of its own instead of the server's (about 28.6 MiB in Kestrel)
            var limit = options.Value.MaxAlternateRequestBytes;
            var body = await ReadBodyAsync(http, limit).ConfigureAwait(false);
            if (body is null)
            {
                await TooLargeAsync(http, limit).ConfigureAwait(false);
                return;
            }

            // A request that is not rewritten reaches the endpoint with its body intact
            request.Body = new MemoryStream(body, writable: false);
            await TryRewriteAsync(http, method, body).ConfigureAwait(false);
        }

        await next(http).ConfigureAwait(false);
    }

    // The whole body, or null once it is longer than limit
    private static async Task<byte[]?> ReadBodyAsync(HttpContext http, long limit)
    {
        var request = http.Request;
        if (request.ContentLength > limit)
        {
            return null;
        }

        RequestReading.SetBodyLimit(http, limit);
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, http.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
    }

    // Answered here, before routing: the headers that XapiRequestFilter puts on every xAPI response are set here.
    // The syntax only exists in xAPI 1.0.3.
    private static Task TooLargeAsync(HttpContext http, long limit)
    {
        http.Response.Headers[XapiHeaders.Version] = XapiVersion.V1_0_3.HeaderValue;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return XapiResults.Problem(StatusCodes.Status413PayloadTooLarge, $"An Alternate Request Syntax request may have at most {limit} bytes of form data.")
            .ExecuteAsync(http);
    }

    private static async Task TryRewriteAsync(HttpContext http, string method, byte[] form)
    {
        var request = http.Request;
        List<KeyValuePair<string, string>> fields;
        try
        {
            fields = await ReadFormAsync(form, http.RequestAborted).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return;
        }

        string? content = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = new List<KeyValuePair<string, string?>>();
        foreach (var (name, value) in fields)
        {
            if (name == ContentField)
            {
                if (content is not null)
                {
                    return;
                }

                content = value;
            }
            else if (HeaderFields.FirstOrDefault(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)) is { } header)
            {
                if (!headers.TryAdd(header, value))
                {
                    return;
                }
            }
            else
            {
                // Every other field is a query parameter (Communication 1.3.s3.b14), spelling kept for 4.1.5 checks
                query.Add(new(name, value));
            }
        }

        // Form fields override real headers (1.3.s3.b7). A 2.0 request is left alone, see AlternateRequestSyntax.
        var version = headers.TryGetValue(XapiHeaders.Version, out var formVersion) ? formVersion : request.Headers[XapiHeaders.Version].ToString();
        if (XapiVersion.TryParseHeader(version, out var parsed) && parsed.IsV2)
        {
            return;
        }

        request.Method = method;
        request.QueryString = QueryString.Create(query);
        foreach (var (name, value) in headers)
        {
            if (!string.Equals(name, HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                request.Headers[name] = value;
            }
        }

        // The content is a UTF-8 string (1.3.s3.b5); without a Content-Type field it is taken to be JSON, the
        // type of every Statement Resource request
        var body = Encoding.UTF8.GetBytes(content ?? "");
        request.Headers.Remove(HeaderNames.TransferEncoding);
        request.Body = new MemoryStream(body, writable: false);
        request.ContentLength = body.Length;
        request.ContentType = headers.TryGetValue(HeaderNames.ContentType, out var contentType)
            ? contentType
            : content is null ? null : "application/json";
    }

    private static async Task<List<KeyValuePair<string, string>>> ReadFormAsync(byte[] form, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>();
        using var reader = new FormReader(new MemoryStream(form, writable: false), Encoding.UTF8)
        {
            // The content field holds whole statement batches; MaxAlternateRequestBytes bounds the form
            ValueLengthLimit = int.MaxValue,
        };
        while (await reader.ReadNextPairAsync(cancellationToken).ConfigureAwait(false) is { } pair)
        {
            fields.Add(pair);
        }

        return fields;
    }

    private static string? Method(string? value) => value?.ToUpperInvariant() switch
    {
        "GET" => HttpMethods.Get,
        "HEAD" => HttpMethods.Head,
        "PUT" => HttpMethods.Put,
        "POST" => HttpMethods.Post,
        "DELETE" => HttpMethods.Delete,
        _ => null,
    };

    // The LRP should send application/x-www-form-urlencoded (1.3.s3.b9); an empty body is a request with no
    // fields at all
    private static bool IsFormOrEmpty(HttpRequest request) =>
        RequestReading.IsMediaType(request, "application/x-www-form-urlencoded") ||
        (request.ContentType is null && request.ContentLength is null or 0);
}
