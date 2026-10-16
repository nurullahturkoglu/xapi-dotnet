using Xapi.Internal;

namespace Xapi;

/// <summary>An individual, identified by exactly one inverse functional identifier.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.1 "Actor as Agent Table".</remarks>
public sealed record Agent : Actor
{
    private readonly InverseFunctionalIdentifier _ifi;

    /// <summary>Creates an Agent.</summary>
    /// <param name="ifi">The identifier.</param>
    public Agent(InverseFunctionalIdentifier ifi) => _ifi = Guard.NotNull(ifi);

    /// <summary>The identifier: <see cref="Mbox"/>, <see cref="MboxSha1Sum"/>, <see cref="OpenId"/> or <see cref="Account"/>.</summary>
    public InverseFunctionalIdentifier Ifi
    {
        get => _ifi;
        init => _ifi = Guard.NotNull(value);
    }

    /// <summary>Creates an Agent identified by e-mail; <c>mailto:</c> is added when missing.</summary>
    /// <param name="email">An e-mail address or <c>mailto:</c> IRI.</param>
    /// <param name="name">The full name, if any.</param>
    /// <returns>The Agent.</returns>
    public static Agent FromMbox(string email, string? name = null) => new(Mbox.FromEmail(email)) { Name = name };

    /// <summary>Creates an Agent identified by an account on another system.</summary>
    /// <param name="homePage">The home page of the system the account is on.</param>
    /// <param name="name">The account's unique id or login name.</param>
    /// <param name="displayName">The Agent's full name, if any.</param>
    /// <returns>The Agent.</returns>
    public static Agent FromAccount(Iri homePage, string name, string? displayName = null) =>
        new(new Account(homePage, name)) { Name = displayName };

    /// <summary>Creates an Agent identified by an account on another system.</summary>
    /// <param name="homePage">The absolute home page URI of the system the account is on.</param>
    /// <param name="name">The account's unique id or login name.</param>
    /// <param name="displayName">The Agent's full name, if any.</param>
    /// <returns>The Agent.</returns>
    public static Agent FromAccount(Uri homePage, string name, string? displayName = null) =>
        FromAccount(Iri.FromUri(homePage), name, displayName);
}
