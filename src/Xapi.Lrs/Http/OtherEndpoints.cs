using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xapi.Json;

namespace Xapi.Lrs.Http;

internal static class AboutEndpoint
{
    // IEEE 9274.1.1, 4.1.6.7: the latest minor and patch version per supported major version
    public static IResult Get(HttpContext http, IOptions<XapiLrsOptions> options)
    {
        if (RequestReading.CheckParameters(http.Request.Query, []) is { } error)
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, error);
        }

        XapiList<string> versions = options.Value.EnableVersion1_0_3 ? ["1.0.3", "2.0.0"] : ["2.0.0"];
        return XapiResults.Json(XapiJson.SerializeAbout(new About(versions), XapiVersion.V2_0_0));
    }
}

internal static class FallbackEndpoint
{
    // The methods of each resource, for the Allow header of a 405 (IEEE 9274.1.1, 4.1, resource table)
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["statements"] = "GET, HEAD, PUT, POST",
        ["about"] = "GET, HEAD",
        ["agents"] = "GET, HEAD",
        ["activities"] = "GET, HEAD",
        ["activities/state"] = "GET, HEAD, PUT, POST, DELETE",
        ["activities/profile"] = "GET, HEAD, PUT, POST, DELETE",
        ["agents/profile"] = "GET, HEAD, PUT, POST, DELETE",
    };

    public static IResult Handle(HttpContext http, string? path)
    {
        var resource = (path ?? "").TrimEnd('/');
        if (Allowed.TryGetValue(resource, out var methods))
        {
            http.Response.Headers[HeaderNames.Allow] = methods;
            return XapiResults.Problem(StatusCodes.Status405MethodNotAllowed, $"{http.Request.Method} is not supported on /{resource}.");
        }

        return XapiResults.Problem(StatusCodes.Status404NotFound, $"This LRS has no resource at /{resource}.");
    }
}
