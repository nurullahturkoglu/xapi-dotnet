using System.Net;

namespace Xapi.Client;

/// <summary>
/// The LRS answered 412 Precondition Failed: the <c>If-Match</c> ETag no longer matches, or
/// <c>If-None-Match: *</c> was sent for a document that exists. The LRS changed nothing (IEEE 9274.1.1, 4.1.4).
/// </summary>
public sealed class XapiPreconditionFailedException : XapiHttpException
{
    /// <summary>Creates an exception with a generic message.</summary>
    public XapiPreconditionFailedException()
        : this("The LRS returned 412 Precondition Failed.", responseBody: null)
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public XapiPreconditionFailedException(string message)
        : this(message, responseBody: null)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public XapiPreconditionFailedException(string message, Exception innerException)
        : base(message, HttpStatusCode.PreconditionFailed, innerException: innerException)
    {
    }

    /// <summary>Creates an exception for a 412 response.</summary>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The response body as text, if any.</param>
    public XapiPreconditionFailedException(string message, string? responseBody)
        : base(message, HttpStatusCode.PreconditionFailed, responseBody)
    {
    }
}
