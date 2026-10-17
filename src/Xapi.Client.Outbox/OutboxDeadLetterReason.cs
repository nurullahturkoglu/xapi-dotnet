namespace Xapi.Client.Outbox;

/// <summary>Why a Statement was moved to the dead letters instead of being delivered.</summary>
/// <remarks>
/// New reasons may be added in a minor release. Code that switches on this enum should handle values it does not
/// know, for example with a default branch, rather than assume the list is complete. <see cref="Rejected"/> is the
/// default value (0), so <c>default(OutboxDeadLetterReason)</c> is a valid reason. <see cref="FileOutboxStore"/>
/// stores reasons by name, not by number.
/// </remarks>
public enum OutboxDeadLetterReason
{
    /// <summary>
    /// The Statement is invalid: the LRS answered 400 Bad Request (or 422) to it alone, or the client's validation
    /// rejected it before sending (for example a 2.0-only property sent to a 1.0.3 LRS).
    /// </summary>
    Rejected,

    /// <summary>The LRS answered 413 Content Too Large to the Statement alone.</summary>
    TooLarge,

    /// <summary>
    /// The LRS answered 409 Conflict, and the Statement it stores under the same id is not equivalent
    /// (<see cref="Validation.StatementEquivalence"/>), or could not be fetched for the comparison.
    /// </summary>
    Conflict,

    /// <summary>The Statement failed with transient errors <see cref="OutboxOptions.MaxAttempts"/> times.</summary>
    MaxAttemptsExceeded,
}
