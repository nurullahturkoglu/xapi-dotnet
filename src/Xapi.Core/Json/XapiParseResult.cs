using System.Diagnostics.CodeAnalysis;

namespace Xapi.Json;

/// <summary>The outcome of reading xAPI JSON: a value, or every error that was found.</summary>
/// <typeparam name="T">The model type.</typeparam>
public sealed class XapiParseResult<T>
{
    private static readonly IReadOnlyList<XapiError> NoErrors = [];

    internal XapiParseResult(T value)
    {
        Value = value;
        Errors = NoErrors;
        IsSuccess = true;
    }

    internal XapiParseResult(IReadOnlyList<XapiError> errors)
    {
        Errors = errors;
    }

    /// <summary>Whether reading (and, in strict mode, validation) succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess { get; }

    /// <summary>The value when <see cref="IsSuccess"/> is <see langword="true"/>; otherwise <see langword="default"/>.</summary>
    public T? Value { get; }

    /// <summary>All errors found; empty on success.</summary>
    public IReadOnlyList<XapiError> Errors { get; }

    /// <summary>Returns the value or throws.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="XapiValidationException">Reading failed; the exception carries <see cref="Errors"/>.</exception>
    public T GetValueOrThrow() => IsSuccess ? Value : throw new XapiValidationException(Errors);
}
