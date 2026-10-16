using System.Net;

namespace Xapi.Client.Outbox.Internal;

/// <summary>What a failed send means for the Statements in it (ADR-0009, docs/outbox.md).</summary>
internal enum FailureKind
{
    /// <summary>The request may succeed later: back off and keep the Statements.</summary>
    Transient,

    /// <summary>The LRS refused the request itself, not the Statements: pause and keep them.</summary>
    Pause,

    /// <summary>409: the LRS has a Statement with one of the ids.</summary>
    Conflict,

    /// <summary>413: the request is too large.</summary>
    TooLarge,

    /// <summary>The Statements are invalid: isolate the bad one and dead-letter it.</summary>
    Rejected,
}

internal static class FailureClassifier
{
    public static FailureKind Classify(Exception exception) => exception switch
    {
        // Thrown by the client before sending, so it is about the Statements
        XapiValidationException => FailureKind.Rejected,
        XapiHttpException http => Classify(http),
        // No xAPI version in common with the LRS (also PlatformNotSupportedException): retrying will not help
        NotSupportedException => FailureKind.Pause,
        // HttpRequestException, timeouts, and anything unexpected (a failing token provider, a handler bug):
        // keeping the Statements is safer than dead-lettering a queue that may be fine
        _ => FailureKind.Transient,
    };

    private static FailureKind Classify(XapiHttpException exception)
    {
        var status = (int)exception.StatusCode;

        // 0 or 2xx: the LRS answered, but not as the standard says (for example a proxy's HTML page). The
        // Statements may or may not be stored; sending them again is safe because their ids are fixed.
        if (exception.IsTransient || status == 0 || status is >= 200 and <= 299)
        {
            return FailureKind.Transient;
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.BadRequest or (HttpStatusCode)422 => FailureKind.Rejected,
            HttpStatusCode.Conflict => FailureKind.Conflict,
            HttpStatusCode.RequestEntityTooLarge => FailureKind.TooLarge,
            // 401, 403, and statuses that mean the endpoint or its configuration is wrong (404, 405, 415, 501,
            // redirects, ...). Dead-lettering would empty a healthy queue one Statement at a time.
            _ => FailureKind.Pause,
        };
    }
}
