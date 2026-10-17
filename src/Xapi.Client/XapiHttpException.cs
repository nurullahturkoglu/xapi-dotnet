using System.Net;

namespace Xapi.Client;

/// <summary>
/// The LRS answered, but not with something the client can use: an error status, or a success status whose body
/// does not follow the standard.
/// </summary>
/// <remarks>
/// Failures below HTTP (DNS, refused connections, timeouts) are not wrapped: they surface as
/// <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/> from <see cref="HttpClient"/>.
/// The client does not retry; see <see cref="IsTransient"/> and <see cref="RetryAfter"/> for callers that do.
/// </remarks>
public class XapiHttpException : Exception
{
    /// <summary>Creates an exception with a generic message and no status code.</summary>
    public XapiHttpException()
        : this("The LRS returned an unusable response.")
    {
    }

    /// <summary>Creates an exception with a message and no status code.</summary>
    /// <param name="message">The message.</param>
    public XapiHttpException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message, an inner exception and no status code.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public XapiHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an LRS response.</summary>
    /// <param name="message">The message.</param>
    /// <param name="statusCode">The response status code.</param>
    /// <param name="responseBody">The response body as text, if any.</param>
    /// <param name="retryAfter">The delay the LRS asked for in a <c>Retry-After</c> header, if any.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public XapiHttpException(string message, HttpStatusCode statusCode, string? responseBody = null, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    /// <summary>The HTTP status code of the response, or 0 when the exception was not created from a response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The response body as text. LRSs usually explain the error here.</summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// The delay the LRS asked for with <c>Retry-After</c> (usually with 429 or 503), already converted from an
    /// HTTP date to a delay where needed. <see langword="null"/> when the header was absent.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Whether the same request might succeed later: 408 Request Timeout, 429 Too Many Requests, and 5xx except
    /// 501 Not Implemented and 505 HTTP Version Not Supported.
    /// </summary>
    public bool IsTransient => StatusCode switch
    {
        HttpStatusCode.RequestTimeout => true,
        (HttpStatusCode)429 => true,
        HttpStatusCode.NotImplemented or HttpStatusCode.HttpVersionNotSupported => false,
        _ => (int)StatusCode is >= 500 and <= 599,
    };
}
