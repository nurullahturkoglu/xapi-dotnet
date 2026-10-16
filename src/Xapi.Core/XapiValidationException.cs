namespace Xapi;

/// <summary>Thrown at API edges (for example <c>GetValueOrThrow</c>) when xAPI data is invalid.</summary>
public sealed class XapiValidationException : Exception
{
    /// <summary>Creates an exception with a generic message and no errors.</summary>
    public XapiValidationException()
        : this("The xAPI data is invalid.")
    {
    }

    /// <summary>Creates an exception with a message and no errors.</summary>
    /// <param name="message">The message.</param>
    public XapiValidationException(string message)
        : base(message) => Errors = [];

    /// <summary>Creates an exception with a message, no errors and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public XapiValidationException(string message, Exception innerException)
        : base(message, innerException) => Errors = [];

    /// <summary>Creates an exception from a list of errors. The message lists the first few.</summary>
    /// <param name="errors">The errors.</param>
    public XapiValidationException(IReadOnlyList<XapiError> errors)
        : base(BuildMessage(errors)) => Errors = errors ?? [];

    /// <summary>The errors that caused the exception.</summary>
    public IReadOnlyList<XapiError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<XapiError> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return "The xAPI data is invalid.";
        }

        const int Shown = 5;
        var lines = errors.Take(Shown).Select(e => "  " + e);
        var more = errors.Count > Shown ? $"{Environment.NewLine}  … and {errors.Count - Shown} more" : string.Empty;
        return $"The xAPI data is invalid ({errors.Count} error(s)):{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{more}";
    }
}
