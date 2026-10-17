using System.Net;

namespace Xapi.Client;

/// <summary>The LRS answered 409 Conflict.</summary>
/// <remarks>
/// <para>
/// For Statements this means the LRS already has a Statement with the same id and it does not match the one sent
/// (IEEE 9274.1.1, 4.1.6.1). The LRS changed nothing. For a batch the response does not say which Statement
/// conflicted; fetch each with <see cref="XapiClient.GetStatementAsync"/> and compare with
/// <see cref="Validation.StatementEquivalence"/>.
/// </para>
/// <para>
/// For documents it means a PUT without <c>If-Match</c> or <c>If-None-Match</c> hit an existing document
/// (IEEE 9274.1.1, 4.1.4). Fetch the document and send its ETag with <see cref="DocumentPrecondition.IfMatch"/>.
/// </para>
/// </remarks>
public sealed class XapiConflictException : XapiHttpException
{
    /// <summary>Creates an exception with a generic message.</summary>
    public XapiConflictException()
        : this("The LRS returned 409 Conflict.", responseBody: null)
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public XapiConflictException(string message)
        : this(message, responseBody: null)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public XapiConflictException(string message, Exception innerException)
        : base(message, HttpStatusCode.Conflict, innerException: innerException)
    {
    }

    /// <summary>Creates an exception for a 409 response.</summary>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The response body as text, if any.</param>
    public XapiConflictException(string message, string? responseBody)
        : base(message, HttpStatusCode.Conflict, responseBody)
    {
    }
}
