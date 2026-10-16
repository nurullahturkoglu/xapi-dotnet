using System.Net.Http.Headers;
using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>
/// An HTTP precondition for writing or deleting a document, so concurrent writers do not overwrite each other
/// (IEEE 9274.1.1, 4.1.4). A failed precondition throws <see cref="XapiPreconditionFailedException"/>.
/// </summary>
public sealed record DocumentPrecondition
{
    private DocumentPrecondition(string headerName, string headerValue)
    {
        HeaderName = headerName;
        HeaderValue = headerValue;
    }

    /// <summary>Only write if the document has not changed since it was read: <c>If-Match: &lt;etag&gt;</c>.</summary>
    /// <remarks>Pass <see cref="XapiDocument.ETag"/> exactly as the LRS returned it, quotes included.</remarks>
    /// <param name="etag">The ETag of the document as last read.</param>
    /// <returns>The precondition.</returns>
    public static DocumentPrecondition IfMatch(string etag) => new("If-Match", Guard.NotEmpty(etag));

    /// <summary>Only write if no document exists yet: <c>If-None-Match: *</c>.</summary>
    public static DocumentPrecondition IfNoneMatchAny { get; } = new("If-None-Match", "*");

    /// <summary>The header name: <c>If-Match</c> or <c>If-None-Match</c>.</summary>
    public string HeaderName { get; }

    /// <summary>The header value: an ETag, or <c>*</c>.</summary>
    public string HeaderValue { get; }

    internal void Apply(HttpRequestHeaders headers) => headers.TryAddWithoutValidation(HeaderName, HeaderValue);
}
