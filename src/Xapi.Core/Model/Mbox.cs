using Xapi.Internal;

namespace Xapi;

/// <summary>An <c>mbox</c> identifier: a <c>mailto:</c> IRI such as <c>mailto:learner@example.com</c>.</summary>
/// <remarks>The format is checked by <see cref="Validation.StatementValidator"/>, not by the constructor.</remarks>
public sealed record Mbox : InverseFunctionalIdentifier
{
    private readonly string _value;

    /// <summary>Creates an <c>mbox</c> identifier.</summary>
    /// <param name="value">The <c>mailto:</c> IRI, used as given.</param>
    public Mbox(string value) => _value = Guard.NotNull(value);

    /// <summary>The <c>mailto:</c> IRI.</summary>
    public string Value
    {
        get => _value;
        init => _value = Guard.NotNull(value);
    }

    /// <summary>Creates an identifier from an e-mail address, adding <c>mailto:</c> when it is missing.</summary>
    /// <param name="email">An e-mail address or <c>mailto:</c> IRI.</param>
    /// <returns>The identifier.</returns>
    public static Mbox FromEmail(string email)
    {
        Guard.NotNull(email);
        return new Mbox(email.StartsWith("mailto:", StringComparison.Ordinal) ? email : "mailto:" + email);
    }
}
