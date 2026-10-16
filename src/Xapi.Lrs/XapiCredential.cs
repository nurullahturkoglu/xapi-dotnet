namespace Xapi.Lrs;

/// <summary>
/// A key and secret that clients send with HTTP Basic authentication, and the scopes granted to it.
/// </summary>
/// <remarks>
/// Configured credentials are written to the database at startup with the secret hashed (PBKDF2), and the
/// stored list then mirrors the configured one. The secret is never stored or logged in plain text.
/// </remarks>
public sealed class XapiCredential
{
    /// <summary>Creates a credential.</summary>
    /// <param name="key">The Basic user name. Must not be empty or contain a colon.</param>
    /// <param name="secret">The Basic password. Must not be empty.</param>
    /// <param name="scopes">The scopes granted. At least one is required.</param>
    /// <exception cref="ArgumentException">A value is empty, the key contains a colon, or no scope is given.</exception>
    public XapiCredential(string key, string secret, IEnumerable<XapiScope> scopes)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(scopes);

        // RFC 7617, 2: the user-id cannot contain a colon, the first colon separates it from the password
        if (key.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("A credential key cannot contain a colon.", nameof(key));
        }

        var list = scopes.Distinct().ToArray();
        if (list.Length == 0)
        {
            throw new ArgumentException("A credential needs at least one scope.", nameof(scopes));
        }

        foreach (var scope in list)
        {
            if (!Enum.IsDefined(scope))
            {
                throw new ArgumentException($"{scope} is not a defined scope.", nameof(scopes));
            }
        }

        Key = key;
        Secret = secret;
        Scopes = list;
    }

    /// <summary>The Basic user name.</summary>
    public string Key { get; }

    /// <summary>The scopes granted to this credential.</summary>
    public IReadOnlyList<XapiScope> Scopes { get; }

    /// <summary>
    /// The authority put on statements stored with this credential. When <see langword="null"/>, an Agent with
    /// an account on <see cref="XapiLrsOptions.AuthorityHomePage"/> named after <see cref="Key"/> is used.
    /// </summary>
    public Agent? Authority { get; init; }

    internal string Secret { get; }
}
