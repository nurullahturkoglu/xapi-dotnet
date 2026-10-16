using Xapi.Internal;

namespace Xapi;

/// <summary>An <c>mbox_sha1sum</c> identifier: the hex SHA-1 of a <c>mailto:</c> IRI.</summary>
/// <remarks>The format is checked by <see cref="Validation.StatementValidator"/>, not by the constructor.</remarks>
public sealed record MboxSha1Sum : InverseFunctionalIdentifier
{
    private readonly string _value;

    /// <summary>Creates an <c>mbox_sha1sum</c> identifier.</summary>
    /// <param name="value">A 40-character hexadecimal SHA-1 digest.</param>
    public MboxSha1Sum(string value) => _value = Guard.NotNull(value);

    /// <summary>The hexadecimal SHA-1 digest.</summary>
    public string Value
    {
        get => _value;
        init => _value = Guard.NotNull(value);
    }
}
